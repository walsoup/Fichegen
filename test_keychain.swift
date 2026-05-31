import Foundation
import Security

let bundleId = "com.itswal.fichegen.app"
let key = "test_api_key"
let value = "my_secret_value"
let tag = "\(bundleId).\(key)".data(using: .utf8)!

let addQuery: [String: Any] = [
    kSecClass as String: kSecClassKey,
    kSecAttrApplicationTag as String: tag,
    kSecValueData as String: value.data(using: .utf8)!
]

SecItemDelete(addQuery as CFDictionary)
let addStatus = SecItemAdd(addQuery as CFDictionary, nil)
print("Add status: \(addStatus)")

let getQuery: [String: Any] = [
    kSecClass as String: kSecClassKey,
    kSecAttrApplicationTag as String: tag,
    kSecReturnData as String: true
]

var item: CFTypeRef?
let getStatus = SecItemCopyMatching(getQuery as CFDictionary, &item)
print("Get status: \(getStatus)")

if getStatus == errSecSuccess, let data = item as? Data {
    print("Retrieved: \(String(data: data, encoding: .utf8) ?? "invalid")")
} else {
    print("Failed to retrieve")
}
