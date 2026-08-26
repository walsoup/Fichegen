/**
 * PROFstudio Serverless Multi-Provider AI Proxy
 * 
 * Powered by Supabase `ai_routes_resolved` view / `get_ai_route` RPC.
 * Fully normalized, zero hardcoded models, zero client configuration.
 */

const SUPABASE_URL = "https://bbodlidtaosxeyeovixe.supabase.co";
const DEFAULT_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJib2RsaWR0YW9zeGV5ZW92aXhlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODc3NTcxODIsImV4cCI6MjEwMzMzMzE4Mn0.qFHCVxa_iBuOFl-hQ29MkTUTacTq7hk58B4cBbf_sME";

// In-memory route cache (TTL: 60 seconds)
let cachedResolvedRoutes = null;
let cachedRoutesExpiry = 0;

export default {
  async fetch(request, env, ctx) {
    if (request.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "POST, GET, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, Authorization, apikey, x-client-info",
        },
      });
    }

    const url = new URL(request.url);

    // Health check & dump of active resolved routes
    if (url.pathname === "/" || url.pathname === "/health") {
      const routes = await fetchResolvedRoutes(env);
      return new Response(JSON.stringify({
        status: "healthy",
        service: "PROFstudio AI Proxy",
        version: "1.3.0",
        routes,
      }, null, 2), {
        headers: { "Content-Type": "application/json" },
      });
    }

    // Chat completions endpoint
    if (url.pathname === "/v1/chat/completions" || url.pathname === "/chat/completions") {
      if (request.method !== "POST") {
        return new Response("Method not allowed", { status: 405 });
      }

      // 1. Authenticate Teacher via Supabase JWT or community access token
      const authHeader = request.headers.get("Authorization");
      let user = null;
      let profile = null;

      if (authHeader && authHeader.startsWith("Bearer ")) {
        const token = authHeader.substring(7);
        user = await verifySupabaseJwt(token, env.SUPABASE_ANON_KEY);
        if (user && user.id) {
          profile = await getTeacherProfile(user.id, token, env);
        }
      }

      const isCommunity = authHeader && authHeader.includes("profstudio-community");
      if (!user && !isCommunity) {
        return new Response(
          JSON.stringify({
            error: {
              message: "Accès refusé. Veuillez vous connecter avec votre compte enseignant PROFstudio.",
              type: "auth_error",
              code: "unauthorized",
            }
          }),
          { status: 401, headers: { "Content-Type": "application/json" } }
        );
      }

      // 1b. Check if teacher account is approved by admin & has remaining quota
      if (profile) {
        if (profile.is_approved === false) {
          return new Response(
            JSON.stringify({
              error: {
                message: "Votre compte enseignant est en attente d'approbation par l'administrateur. Veuillez patienter.",
                code: "account_pending_approval",
              }
            }),
            { status: 403, headers: { "Content-Type": "application/json" } }
          );
        }

        if (typeof profile.monthly_quota === "number" && profile.monthly_quota <= 0) {
          return new Response(
            JSON.stringify({
              error: {
                message: "Quota mensuel de générations atteint. Contactez votre établissement ou l'administrateur.",
                code: "quota_exceeded",
              }
            }),
            { status: 429, headers: { "Content-Type": "application/json" } }
          );
        }
      }

      // 2. Parse request payload
      const body = await request.json();
      const isStreaming = body.stream === true;
      const requestedTask = (body.model || "fiche").trim().toLowerCase();

      // 3. Resolve route from the Supabase `ai_routes_resolved` view
      const resolvedRoute = await resolveRouteForTask(requestedTask, env);

      if (!resolvedRoute) {
        return new Response(
          JSON.stringify({
            error: {
              message: `Aucune route IA configurée pour la tâche '${requestedTask}'. Veuillez configurer 'ai_routes' dans Supabase.`,
              code: "no_route",
            }
          }),
          { status: 500, headers: { "Content-Type": "application/json" } }
        );
      }

      // 4. Primary attempt
      try {
        return await executeProviderCall({
          baseUrl: resolvedRoute.base_url,
          apiStyle: resolvedRoute.api_style,
          apiKeyEnv: resolvedRoute.api_key_env,
          model: resolvedRoute.model,
          body,
          env,
          isStreaming,
        });
      } catch (primaryErr) {
        console.warn(`[Proxy] Échec provider ${resolvedRoute.provider} (${resolvedRoute.model}):`, primaryErr.message);

        // 5. Automatic Fallback attempt if configured and enabled
        if (resolvedRoute.fallback_provider && resolvedRoute.fallback_enabled && resolvedRoute.fallback_model && resolvedRoute.fallback_base_url) {
          console.info(`[Proxy] Basculement sur le fallback ${resolvedRoute.fallback_provider} (${resolvedRoute.fallback_model})`);
          try {
            return await executeProviderCall({
              baseUrl: resolvedRoute.fallback_base_url,
              apiStyle: resolvedRoute.fallback_api_style || "openai",
              apiKeyEnv: resolvedRoute.fallback_api_key_env,
              model: resolvedRoute.fallback_model,
              body,
              env,
              isStreaming,
            });
          } catch (fallbackErr) {
            return new Response(
              JSON.stringify({
                error: {
                  message: `Échec du fournisseur principal (${primaryErr.message}) et du fallback (${fallbackErr.message})`,
                  code: "all_providers_failed",
                }
              }),
              { status: 502, headers: { "Content-Type": "application/json" } }
            );
          }
        }

        return new Response(
          JSON.stringify({ error: { message: primaryErr.message } }),
          { status: 500, headers: { "Content-Type": "application/json" } }
        );
      }
    }

    return new Response("Not found", { status: 404 });
  },
};

// ───────────────────────── Dispatcher selon api_style ─────────────────────────

async function executeProviderCall({ baseUrl, apiStyle, apiKeyEnv, model, body, env, isStreaming }) {
  const apiKey = apiKeyEnv ? env[apiKeyEnv] : null;
  const style = (apiStyle || "openai").toLowerCase();

  if (style === "anthropic") {
    return callAnthropicApi(baseUrl, model, body, apiKey, isStreaming);
  }

  if (style === "google") {
    return callGoogleNativeApi(baseUrl, model, body, apiKey, isStreaming);
  }

  // Standard OpenAI-compatible format (covers DoubleWord, OpenRouter, DeepSeek, Groq, Mistral, OpenAI, Together, Fireworks, Cerebras, xAI, Local, etc.)
  return callOpenAiCompatibleApi(baseUrl, model, body, apiKey, isStreaming);
}

// ───────────────────────── OpenAI Compatible Implementation ─────────────────────────

async function callOpenAiCompatibleApi(baseUrl, model, body, apiKey, isStreaming) {
  if (!baseUrl) throw new Error("baseUrl non définie pour ce provider.");

  const endpoint = `${baseUrl.replace(/\/+$/, "")}/chat/completions`;
  const headers = { "Content-Type": "application/json" };
  if (apiKey) headers["Authorization"] = `Bearer ${apiKey}`;

  const payload = {
    model: model,
    messages: body.messages || [],
    temperature: body.temperature,
    max_tokens: body.max_tokens,
    stream: isStreaming,
    response_format: body.response_format,
  };

  const res = await fetch(endpoint, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
  });

  if (!res.ok) {
    const err = await res.text();
    throw new Error(`[${endpoint}] HTTP ${res.status}: ${err}`);
  }

  return new Response(res.body, {
    headers: {
      "Content-Type": isStreaming ? "text/event-stream" : "application/json",
      "Access-Control-Allow-Origin": "*",
    },
  });
}

// ───────────────────────── Anthropic API Implementation ─────────────────────────

async function callAnthropicApi(baseUrl, model, body, apiKey, isStreaming) {
  if (!apiKey) throw new Error("Clé API Anthropic non configurée.");
  const endpoint = `${(baseUrl || "https://api.anthropic.com/v1").replace(/\/+$/, "")}/messages`;

  let systemPrompt = "";
  const anthropicMessages = [];
  for (const m of body.messages || []) {
    if (m.role === "system") {
      systemPrompt += (systemPrompt ? "\n" : "") + m.content;
    } else {
      anthropicMessages.push({ role: m.role, content: m.content });
    }
  }

  const payload = {
    model: model,
    max_tokens: body.max_tokens ?? 4096,
    temperature: body.temperature,
    messages: anthropicMessages,
    stream: isStreaming,
  };
  if (systemPrompt) payload.system = systemPrompt;

  const res = await fetch(endpoint, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "x-api-key": apiKey,
      "anthropic-version": "2023-06-01",
    },
    body: JSON.stringify(payload),
  });

  if (!res.ok) {
    const err = await res.text();
    throw new Error(`[Anthropic] HTTP ${res.status}: ${err}`);
  }

  if (!isStreaming) {
    const json = await res.json();
    const text = json.content?.[0]?.text || "";
    return new Response(JSON.stringify({
      id: json.id,
      object: "chat.completion",
      created: Math.floor(Date.now() / 1000),
      model,
      choices: [{ index: 0, message: { role: "assistant", content: text }, finish_reason: "stop" }],
    }), { headers: { "Content-Type": "application/json" } });
  }

  // Transform Anthropic SSE stream to OpenAI standard format for client
  const { readable, writable } = new TransformStream({
    transform(chunk, controller) {
      const text = new TextDecoder().decode(chunk);
      for (const line of text.split("\n")) {
        if (line.startsWith("data: ")) {
          try {
            const data = JSON.parse(line.substring(6));
            if (data.type === "content_block_delta" && data.delta?.text) {
              const chunkObj = {
                id: `chatcmpl-${Date.now()}`,
                object: "chat.completion.chunk",
                model,
                choices: [{ index: 0, delta: { content: data.delta.text }, finish_reason: null }],
              };
              controller.enqueue(new TextEncoder().encode(`data: ${JSON.stringify(chunkObj)}\n\n`));
            }
          } catch {}
        }
      }
    },
    flush(controller) {
      controller.enqueue(new TextEncoder().encode("data: [DONE]\n\n"));
    },
  });

  res.body.pipeTo(writable);
  return new Response(readable, {
    headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", "Access-Control-Allow-Origin": "*" },
  });
}

// ───────────────────────── Google Native API Implementation ─────────────────────────

async function callGoogleNativeApi(baseUrl, model, body, apiKey, isStreaming) {
  if (!apiKey) throw new Error("Clé API Google non configurée.");
  const rootUrl = baseUrl || "https://generativelanguage.googleapis.com/v1beta";
  const url = isStreaming
    ? `${rootUrl.replace(/\/+$/, "")}/models/${model}:streamGenerateContent?alt=sse&key=${apiKey}`
    : `${rootUrl.replace(/\/+$/, "")}/models/${model}:generateContent?key=${apiKey}`;

  const contents = [];
  let systemInstruction = body.system_instruction ? { parts: [{ text: body.system_instruction }] } : undefined;

  for (const m of body.messages || []) {
    if (m.role === "system") {
      systemInstruction = { parts: [{ text: m.content }] };
    } else {
      contents.push({
        role: m.role === "assistant" ? "model" : "user",
        parts: [{ text: m.content }],
      });
    }
  }

  const payload = {
    contents,
    generationConfig: {
      temperature: body.temperature,
      maxOutputTokens: body.max_tokens,
    },
  };
  if (systemInstruction) payload.systemInstruction = systemInstruction;

  const res = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });

  if (!res.ok) {
    const err = await res.text();
    throw new Error(`[Google] HTTP ${res.status}: ${err}`);
  }

  if (!isStreaming) {
    const json = await res.json();
    const text = json.candidates?.[0]?.content?.parts?.[0]?.text || "";
    return new Response(JSON.stringify({
      id: `chatcmpl-${Date.now()}`,
      object: "chat.completion",
      created: Math.floor(Date.now() / 1000),
      model,
      choices: [{ index: 0, message: { role: "assistant", content: text }, finish_reason: "stop" }],
    }), { headers: { "Content-Type": "application/json" } });
  }

  const { readable, writable } = new TransformStream({
    transform(chunk, controller) {
      const text = new TextDecoder().decode(chunk);
      for (const line of text.split("\n")) {
        if (line.startsWith("data: ")) {
          try {
            const data = JSON.parse(line.substring(6));
            const delta = data.candidates?.[0]?.content?.parts?.[0]?.text || "";
            if (delta) {
              const chunkObj = {
                id: `chatcmpl-${Date.now()}`,
                object: "chat.completion.chunk",
                model,
                choices: [{ index: 0, delta: { content: delta }, finish_reason: null }],
              };
              controller.enqueue(new TextEncoder().encode(`data: ${JSON.stringify(chunkObj)}\n\n`));
            }
          } catch {}
        }
      }
    },
    flush(controller) {
      controller.enqueue(new TextEncoder().encode("data: [DONE]\n\n"));
    },
  });

  res.body.pipeTo(writable);
  return new Response(readable, {
    headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", "Access-Control-Allow-Origin": "*" },
  });
}

// ───────────────────────── Route Resolution via Supabase ─────────────────────────

async function resolveRouteForTask(task, env) {
  const routes = await fetchResolvedRoutes(env);
  if (!routes || routes.length === 0) return null;

  // Exact match on task name
  const match = routes.find(r => r.task.toLowerCase() === task && r.provider_enabled);
  if (match) return match;

  // Fallback to '*' wildcard route
  const wildcard = routes.find(r => r.task === "*" && r.provider_enabled);
  if (wildcard) return wildcard;

  // Return first enabled route
  return routes.find(r => r.provider_enabled) || null;
}

async function fetchResolvedRoutes(env) {
  const now = Date.now();
  if (cachedResolvedRoutes && now < cachedRoutesExpiry) {
    return cachedResolvedRoutes;
  }

  try {
    const supabaseUrl = env.SUPABASE_URL || SUPABASE_URL;
    const anonKey = env.SUPABASE_ANON_KEY || "dummy_key";

    // Read directly from the `ai_routes_resolved` view
    const res = await fetch(`${supabaseUrl}/rest/v1/ai_routes_resolved?select=*`, {
      headers: { apikey: anonKey, Authorization: `Bearer ${anonKey}` },
    });

    if (res.ok) {
      const rows = await res.json();
      if (Array.isArray(rows) && rows.length > 0) {
        cachedResolvedRoutes = rows;
        cachedRoutesExpiry = now + 60000; // cache for 60 seconds
        return cachedResolvedRoutes;
      }
    }
  } catch (e) {
    console.warn("Could not query ai_routes_resolved view:", e.message);
  }

  return cachedResolvedRoutes || [];
}

async function verifySupabaseJwt(token, anonKey) {
  try {
    const res = await fetch(`${SUPABASE_URL}/auth/v1/user`, {
      headers: { Authorization: `Bearer ${token}`, apikey: anonKey || "dummy" },
    });
    if (res.ok) return await res.json();
  } catch {}
  return null;
}

async function getTeacherProfile(userId, token, env) {
  try {
    const supabaseUrl = env.SUPABASE_URL || SUPABASE_URL;
    const anonKey = env.SUPABASE_ANON_KEY || "dummy";
    const res = await fetch(`${supabaseUrl}/rest/v1/profiles?id=eq.${userId}&select=*`, {
      headers: {
        Authorization: `Bearer ${token}`,
        apikey: anonKey,
      },
    });
    if (res.ok) {
      const rows = await res.json();
      if (Array.isArray(rows) && rows.length > 0) return rows[0];
    }
  } catch (e) {
    console.warn("Could not fetch teacher profile:", e.message);
  }
  return null;
}
