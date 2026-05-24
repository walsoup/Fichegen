import Foundation
struct ChatMessage: Identifiable, Equatable {
    let id = UUID()
    let role: String // "user" or "assistant"
    let text: String
    let timestamp = Date()
}
let msg = ChatMessage(role: "user", text: "hello")
print(msg.id)
