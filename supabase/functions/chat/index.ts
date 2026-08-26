// Supabase Edge Function (Deno / TypeScript) — PROFstudio Normalized Multi-Provider AI Proxy
// Reads from view `public.ai_routes_resolved` and dispatches by `api_style`.

import { serve } from "https://deno.land/std@0.168.0/http/server.ts";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL") ?? "https://bbodlidtaosxeyeovixe.supabase.co";
const DEFAULT_ANON_KEY = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJib2RsaWR0YW9zeGV5ZW92aXhlIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODc3NTcxODIsImV4cCI6MjEwMzMzMzE4Mn0.qFHCVxa_iBuOFl-hQ29MkTUTacTq7hk58B4cBbf_sME";

serve(async (req: Request) => {
  try {
    if (req.method === "OPTIONS") {
      return new Response(null, {
        headers: {
          "Access-Control-Allow-Origin": "*",
          "Access-Control-Allow-Methods": "POST, GET, OPTIONS",
          "Access-Control-Allow-Headers": "Content-Type, Authorization, apikey, x-client-info",
        },
      });
    }

    const url = new URL(req.url);

    // Confirmation landing page for email verification redirects
    if (req.method === "GET") {
      const html = `<!DOCTYPE html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <title>PROFstudio — Confirmation de compte</title>
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>
    * { box-sizing: border-box; }
    body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #0F172A; color: #F8FAFC; display: flex; align-items: center; justify-content: center; min-height: 100vh; margin: 0; padding: 20px; }
    .card { background: #1E293B; border: 1px solid #334155; border-radius: 16px; padding: 36px; text-align: center; max-width: 440px; box-shadow: 0 20px 25px -5px rgba(0,0,0,0.5); }
    .icon { font-size: 48px; margin-bottom: 14px; }
    h1 { font-size: 22px; margin: 0 0 12px 0; color: #38BDF8; }
    p { font-size: 14.5px; color: #94A3B8; line-height: 1.6; margin: 0 0 20px 0; }
    .badge { display: inline-block; background: #065F46; color: #34D399; font-size: 13px; font-weight: 600; padding: 6px 12px; border-radius: 9999px; margin-bottom: 16px; }
  </style>
</head>
<body>
  <div class="card">
    <div id="icon" class="icon">✅</div>
    <div id="badge" class="badge">Compte Enseignant Validé</div>
    <h1 id="title">Adresse e-mail confirmée !</h1>
    <p id="msg">Votre compte enseignant PROFstudio est désormais actif.<br><br>Vous pouvez retourner dans l'application Windows et vous connecter avec votre mot de passe.</p>
  </div>
  <script>
    const hash = window.location.hash || '';
    if (hash.includes('error=')) {
      if (hash.includes('otp_expired')) {
        document.getElementById('icon').textContent = 'ℹ️';
        document.getElementById('badge').textContent = 'Lien déjà utilisé ou expiré';
        document.getElementById('badge').style.background = '#1E3A8A';
        document.getElementById('badge').style.color = '#93C5FD';
        document.getElementById('title').textContent = 'Lien déjà validé';
        document.getElementById('msg').innerHTML = 'Ce lien de confirmation a déjà été utilisé ou votre compte est déjà vérifié.<br><br>Vous pouvez vous connecter directement dans l\\'application PROFstudio.';
      }
    }
  </script>
</body>
</html>`;
      return new Response(html, {
        status: 200,
        headers: new Headers({
          "Content-Type": "text/html; charset=utf-8",
          "Cache-Control": "no-cache",
        }),
      });
    }

    // 1. Auth check
    const authHeader = req.headers.get("Authorization");
    const isCommunity = authHeader?.includes("profstudio-community");
    let user: any = null;
    let profile: any = null;

    if (authHeader && authHeader.startsWith("Bearer ") && !isCommunity) {
      const token = authHeader.substring(7);
      const anonKey = Deno.env.get("SUPABASE_ANON_KEY") || DEFAULT_ANON_KEY;
      try {
        const userRes = await fetch(`${SUPABASE_URL}/auth/v1/user`, {
          headers: { Authorization: `Bearer ${token}`, apikey: anonKey },
        });
        if (userRes.ok) {
          user = await userRes.json();
          if (user?.id) {
            const profRes = await fetch(`${SUPABASE_URL}/rest/v1/profiles?id=eq.${user.id}&select=*`, {
              headers: { Authorization: `Bearer ${token}`, apikey: anonKey },
            });
            if (profRes.ok) {
              const profs = await profRes.json();
              if (Array.isArray(profs) && profs.length > 0) profile = profs[0];
            }
          }
        }
      } catch {}
    }

    if (!user && !isCommunity) {
      return new Response(JSON.stringify({ error: { message: "Accès refusé. Veuillez vous connecter avec votre compte enseignant PROFstudio." } }), {
        status: 401,
        headers: { "Content-Type": "application/json" },
      });
    }

    // Check access & quota
    if (profile) {
      if (profile.is_approved === false) {
        return new Response(JSON.stringify({ error: { message: "Votre compte enseignant est en attente d'approbation par l'administrateur." } }), {
          status: 403,
          headers: { "Content-Type": "application/json" },
        });
      }
      if (typeof profile.monthly_quota === "number" && profile.monthly_quota <= 0) {
        return new Response(JSON.stringify({ error: { message: "Quota mensuel de générations atteint." } }), {
          status: 429,
          headers: { "Content-Type": "application/json" },
        });
      }
    }

    const body = await req.json();
    const isStreaming = body.stream === true;
    const requestedTask = (body.model || "fiche").trim().toLowerCase();

    // 2. Fetch resolved route from view `ai_routes_resolved`
    const anonKey = Deno.env.get("SUPABASE_ANON_KEY") || DEFAULT_ANON_KEY;
    let route: any = null;

    try {
      const res = await fetch(`${SUPABASE_URL}/rest/v1/ai_routes_resolved?select=*`, {
        headers: { apikey: anonKey, Authorization: `Bearer ${anonKey}` },
      });
      if (res.ok) {
        const rows = await res.json();
        route = rows.find((r: any) => r.task.toLowerCase() === requestedTask && r.provider_enabled)
             || rows.find((r: any) => r.task === "*" && r.provider_enabled)
             || rows[0];
      }
    } catch {}

    if (!route) {
      return new Response(JSON.stringify({ error: { message: `Aucune route trouvée pour '${requestedTask}'` } }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      });
    }

    // 3. Execute Primary with Fallback
    try {
      return await executeCall(route.base_url, route.api_style, route.api_key_env, route.model, body, isStreaming);
    } catch (primaryErr: any) {
      console.warn(`[Proxy] Erreur provider ${route.provider}:`, primaryErr.message);

      if (route.fallback_provider && route.fallback_enabled && route.fallback_model && route.fallback_base_url) {
        try {
          return await executeCall(route.fallback_base_url, route.fallback_api_style || "openai", route.fallback_api_key_env, route.fallback_model, body, isStreaming);
        } catch (fallbackErr: any) {
          return new Response(JSON.stringify({ error: { message: `Échec principal (${primaryErr.message}) et fallback (${fallbackErr.message})` } }), {
            status: 502,
            headers: { "Content-Type": "application/json" },
          });
        }
      }

      return new Response(JSON.stringify({ error: { message: primaryErr.message } }), {
        status: 500,
        headers: { "Content-Type": "application/json" },
      });
    }
  } catch (globalErr: any) {
    return new Response(JSON.stringify({ error: { message: `Unhandled server error: ${globalErr.message}`, stack: globalErr.stack } }), {
      status: 500,
      headers: { "Content-Type": "application/json" },
    });
  }
});

async function executeCall(baseUrl: string, apiStyle: string, apiKeyEnv: string, model: string, body: any, isStreaming: boolean) {
  const apiKey = apiKeyEnv ? Deno.env.get(apiKeyEnv) : null;
  const style = (apiStyle || "openai").toLowerCase();

  if (style === "anthropic") {
    return callAnthropic(baseUrl, model, body, apiKey, isStreaming);
  }

  // Standard OpenAI compatible (DoubleWord, OpenRouter, DeepSeek, Groq, Mistral, OpenAI, Together, Fireworks, Cerebras, xAI, etc.)
  const endpoint = `${baseUrl.replace(/\/+$/, "")}/chat/completions`;
  const headers: Record<string, string> = { "Content-Type": "application/json" };
  if (apiKey) headers["Authorization"] = `Bearer ${apiKey}`;

  const payload: any = {
    model: model,
    messages: body.messages || [],
    stream: isStreaming,
  };
  if (typeof body.temperature === "number") payload.temperature = body.temperature;
  if (typeof body.max_tokens === "number") payload.max_tokens = body.max_tokens;
  if (body.response_format) payload.response_format = body.response_format;

  const res = await fetch(endpoint, {
    method: "POST",
    headers,
    body: JSON.stringify(payload),
  });

  if (!res.ok) {
    const errText = await res.text();
    throw new Error(`[${endpoint}] status ${res.status}: ${errText}`);
  }

  return new Response(res.body, {
    headers: {
      "Content-Type": isStreaming ? "text/event-stream" : "application/json",
      "Cache-Control": "no-cache",
      "Connection": "keep-alive",
      "Access-Control-Allow-Origin": "*",
    },
  });
}

async function callAnthropic(baseUrl: string, model: string, body: any, apiKey: string | null, isStreaming: boolean) {
  if (!apiKey) throw new Error("Clé API Anthropic manquante.");
  const endpoint = `${(baseUrl || "https://api.anthropic.com/v1").replace(/\/+$/, "")}/messages`;

  let systemPrompt = "";
  const anthropicMessages: any[] = [];
  for (const m of body.messages || []) {
    if (m.role === "system") {
      systemPrompt += (systemPrompt ? "\n" : "") + m.content;
    } else {
      anthropicMessages.push({ role: m.role, content: m.content });
    }
  }

  const payload: any = {
    model,
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
    const errText = await res.text();
    throw new Error(`[Anthropic] status ${res.status}: ${errText}`);
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

  res.body?.pipeTo(writable);
  return new Response(readable, {
    headers: { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", "Access-Control-Allow-Origin": "*" },
  });
}
