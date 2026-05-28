import Foundation
import PDFKit

/// Native service using PDFKit to inspect guidebooks and textbooks.
struct PDFProcessor {
    
    struct ToCEntry: Codable, Hashable {
        let topic: String
        let page: Int

        /// Pages to extract for this entry: from this entry's page up to (but not including) the next.
        /// Populated by the caller after loading the full TOC list.
        var pages: [Int] { [page] }   // single-page default; caller can override via computePageRanges
    }

    /// Expand a flat TOC list so each entry covers its page through the next entry's page minus 1
    /// (capped at `maxSpread` pages to avoid massive extractions).
    static func computePageRanges(for entries: [ToCEntry], maxSpread: Int = 6) -> [(entry: ToCEntry, pages: [Int])] {
        guard !entries.isEmpty else { return [] }
        var result: [(entry: ToCEntry, pages: [Int])] = []
        for (i, entry) in entries.enumerated() {
            let start = entry.page
            let endPage = i + 1 < entries.count ? min(entries[i + 1].page - 1, start + maxSpread - 1) : start + maxSpread - 1
            let pageRange = Array(start...max(start, endPage))
            result.append((entry, pageRange))
        }
        return result
    }
    
    // MARK: - Find Files
    
    static func findGuideFile(classLevel: String, guidesDir: String) -> URL? {
        let fileManager = FileManager.default
        let classLower = classLevel.lowercased()
        
        var possibleNames = ["guide_pedagogique_\(classLower).pdf"]
        if classLower == "6e" {
            possibleNames.append("guide_pedagogique_6eme.pdf")
        }
        
        let folderURL = URL(fileURLWithPath: (guidesDir as NSString).expandingTildeInPath)
        for name in possibleNames {
            let fileURL = folderURL.appendingPathComponent(name)
            if fileManager.fileExists(atPath: fileURL.path) {
                return fileURL
            }
        }
        return nil
    }
    
    static func findTextbookFile(classLevel: String, textbookDir: String) -> URL? {
        guard !textbookDir.isEmpty else { return nil }
        let fileManager = FileManager.default
        let classLower = classLevel.lowercased()
        
        var possibleNames = [
            "livre_\(classLower).pdf",
            "manuel_\(classLower).pdf",
            "textbook_\(classLower).pdf",
            "\(classLower).pdf"
        ]
        if classLower == "6e" {
            possibleNames.append(contentsOf: [
                "livre_6eme.pdf", "manuel_6eme.pdf", "textbook_6eme.pdf", "6eme.pdf"
            ])
        }
        
        let folderURL = URL(fileURLWithPath: (textbookDir as NSString).expandingTildeInPath)
        for name in possibleNames {
            let fileURL = folderURL.appendingPathComponent(name)
            if fileManager.fileExists(atPath: fileURL.path) {
                return fileURL
            }
        }
        return nil
    }
    
    // MARK: - Cache Operations
    
    static func getCacheURL(pdfURL: URL, guidesDir: String) -> URL {
        let folderURL = URL(fileURLWithPath: (guidesDir as NSString).expandingTildeInPath)
        let cacheDir = folderURL.appendingPathComponent("toc_cache")
        try? FileManager.default.createDirectory(at: cacheDir, withIntermediateDirectories: true, attributes: nil)
        return cacheDir.appendingPathComponent(pdfURL.lastPathComponent + ".json")
    }
    
    static func loadCachedTOC(pdfURL: URL, guidesDir: String) -> [ToCEntry]? {
        let cacheURL = getCacheURL(pdfURL: pdfURL, guidesDir: guidesDir)
        let fileManager = FileManager.default
        
        guard fileManager.fileExists(atPath: cacheURL.path) else { return nil }
        
        // Staleness check: if cache file is older than source PDF, invalidate it
        if let cacheAttributes = try? fileManager.attributesOfItem(atPath: cacheURL.path),
           let pdfAttributes = try? fileManager.attributesOfItem(atPath: pdfURL.path),
           let cacheDate = cacheAttributes[.modificationDate] as? Date,
           let pdfDate = pdfAttributes[.modificationDate] as? Date {
            if cacheDate < pdfDate {
                try? fileManager.removeItem(at: cacheURL)
                return nil
            }
        }
        
        guard let data = try? Data(contentsOf: cacheURL) else { return nil }
        let decoder = JSONDecoder()
        do {
            return try decoder.decode([ToCEntry].self, from: data)
        } catch {
            try? fileManager.removeItem(at: cacheURL)
            return nil
        }
    }
    
    static func saveTOCToCache(pdfURL: URL, guidesDir: String, toc: [ToCEntry]) {
        let cacheURL = getCacheURL(pdfURL: pdfURL, guidesDir: guidesDir)
        let encoder = JSONEncoder()
        encoder.outputFormatting = .prettyPrinted
        
        guard let data = try? encoder.encode(toc) else { return }
        
        do {
            try data.write(to: cacheURL, options: .atomic)
        } catch {
            print("Failed to cache TOC: \(error)")
        }
    }
    
    // MARK: - PDF kit parsing
    
    static func extractRawTOCText(pdfURL: URL, maxPages: Int = 12) -> String? {
        guard let document = PDFDocument(url: pdfURL) else { return nil }
        var fullText = ""
        let scanLimit = min(maxPages, document.pageCount)
        
        for i in 0..<scanLimit {
            if let page = document.page(at: i), let text = page.string {
                fullText += text + "\n\n"
            }
        }
        return fullText.isEmpty ? nil : fullText
    }
    
    // MARK: - Heuristics TOC parser
    
    static func parseTOCWithHeuristics(tocText: String) -> [ToCEntry]? {
        var entries: [ToCEntry] = []
        var seen = Set<String>()
        
        // Define patterns
        // Pattern 1: Topic ............ 23
        let pattern1 = try? NSRegularExpression(pattern: "^\\s*(.{4,150}?)\\s*(?:\\.{2,}|\\s{2,})\\s*(\\d{1,4})\\s*$", options: [])
        // Pattern 2: Topic 23 (loose)
        let pattern2 = try? NSRegularExpression(pattern: "^\\s*(.{6,150}?)\\s+(\\d{1,4})\\s*$", options: [])
        // Pattern 3: 23 Topic
        let pattern3 = try? NSRegularExpression(pattern: "^\\s*(\\d{1,4})\\s+(.{4,150})\\s*$", options: [])
        
        let lines = tocText.components(separatedBy: .newlines)
        
        for line in lines {
            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.count < 6 { continue }
            
            var foundTopic = ""
            var foundPage = -1
            let range = NSRange(location: 0, length: trimmed.utf16.count)
            
            if let match1 = pattern1?.firstMatch(in: trimmed, options: [], range: range) {
                if let topicRange = Range(match1.range(at: 1), in: trimmed),
                   let pageRange = Range(match1.range(at: 2), in: trimmed) {
                    foundTopic = String(trimmed[topicRange])
                    foundPage = Int(trimmed[pageRange]) ?? -1
                }
            } else if let match2 = pattern2?.firstMatch(in: trimmed, options: [], range: range) {
                if let topicRange = Range(match2.range(at: 1), in: trimmed),
                   let pageRange = Range(match2.range(at: 2), in: trimmed) {
                    foundTopic = String(trimmed[topicRange])
                    foundPage = Int(trimmed[pageRange]) ?? -1
                }
            } else if let match3 = pattern3?.firstMatch(in: trimmed, options: [], range: range) {
                if let pageRange = Range(match3.range(at: 1), in: trimmed),
                   let topicRange = Range(match3.range(at: 2), in: trimmed) {
                    foundTopic = String(trimmed[topicRange])
                    foundPage = Int(trimmed[pageRange]) ?? -1
                }
            }
            
            // Clean topic string
            let cleanTopic = foundTopic
                .trimmingCharacters(in: CharacterSet(charactersIn: " .-\t\n\r"))
                .replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
            
            if cleanTopic.isEmpty || foundPage <= 0 || foundPage > 2000 {
                continue
            }
            
            let key = "\(cleanTopic.lowercased())_\(foundPage)"
            if seen.contains(key) { continue }
            seen.insert(key)
            
            entries.append(ToCEntry(topic: cleanTopic, page: foundPage))
        }
        
        // Sort entries by page number
        entries.sort { $0.page < $1.page }
        
        // Keep only ordered pages if possible
        var filtered: [ToCEntry] = []
        var lastPage = -1
        for entry in entries {
            if entry.page >= lastPage {
                filtered.append(entry)
                lastPage = entry.page
            }
        }
        
        let result = filtered.isEmpty ? entries : filtered
        return result.isEmpty ? nil : result
    }
    
    // MARK: - Offset detection
    
    static func detectPageOffset(pdfURL: URL) -> Int {
        guard let document = PDFDocument(url: pdfURL) else { return 0 }
        let n = document.pageCount
        if n == 0 { return 0 }
        
        let startIdx = min(12, n - 1)
        let endIdx = min(startIdx + 15, n)
        
        var deltaCounts: [Int: Int] = [:]
        var hits = 0
        
        let explicitPagePattern = try? NSRegularExpression(pattern: "(?i)\\b(?:page|p\\.?|pag\\.)\\s*([0-9]{1,4})\\b", options: [])
        let romanPagePattern = try? NSRegularExpression(pattern: "(?i)\\b(?:page|p\\.?|pag\\.)\\s*([ivxlcdm]{1,7})\\b", options: [])
        
        for i in startIdx..<endIdx {
            guard let page = document.page(at: i) else { continue }
            let physicalPage = i + 1
            
            // Extract text from page
            guard let fullText = page.string else { continue }
            let lines = fullText.components(separatedBy: .newlines)
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                .filter { !$0.isEmpty }
            
            var candidates: [Int] = []
            
            // Check top/bottom lines for bare numbers
            let linesToScan = Array(lines.prefix(2)) + Array(lines.suffix(2))
            for line in linesToScan {
                if line.count >= 1 && line.count <= 4 {
                    if let val = Int(line) {
                        candidates.append(val)
                    } else if let val = romanToInt(line) {
                        candidates.append(val)
                    }
                }
            }
            
            // Scan full text for explicit "Page X" labels
            if candidates.isEmpty {
                let range = NSRange(location: 0, length: fullText.utf16.count)
                explicitPagePattern?.enumerateMatches(in: fullText, options: [], range: range) { match, _, _ in
                    if let match = match,
                       let numRange = Range(match.range(at: 1), in: fullText),
                       let val = Int(fullText[numRange]) {
                        candidates.append(val)
                    }
                }
                
                romanPagePattern?.enumerateMatches(in: fullText, options: [], range: range) { match, _, _ in
                    if let match = match,
                       let romanRange = Range(match.range(at: 1), in: fullText),
                       let val = romanToInt(String(fullText[romanRange])) {
                        candidates.append(val)
                    }
                }
            }
            
            for printed in candidates {
                if printed <= 0 || printed >= 2000 { continue }
                let delta = physicalPage - printed
                if delta >= -50 && delta <= 200 {
                    deltaCounts[delta, default: 0] += 1
                    hits += 1
                }
            }
        }
        
        if hits > 0 {
            var bestDelta = 0
            var bestCount = -1
            for (d, c) in deltaCounts {
                if c > bestCount || (c == bestCount && abs(d) < abs(bestDelta)) {
                    bestDelta = d
                    bestCount = c
                }
            }
            
            let threshold = (endIdx - startIdx) > 5 ? 2 : 1
            if bestCount >= threshold {
                return bestDelta
            }
        }
        
        return 0
    }
    
    // MARK: - Roman Numeral Conversion

    private static func romanToInt(_ s: String) -> Int? {
        let clean = s.uppercased().trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { return nil }

        let romanMap: [Character: Int] = ["I": 1, "V": 5, "X": 10, "L": 50, "C": 100, "D": 500, "M": 1000]
        var total = 0
        var prevValue = 0

        for ch in clean.reversed() {
            guard let val = romanMap[ch] else { return nil }
            if val < prevValue {
                total -= val
            } else {
                total += val
                prevValue = val
            }
        }
        return total > 0 ? total : nil
    }

    // MARK: - Text Extraction

    /// Extract concatenated text from the given 1-based page numbers in a PDF document.
    static func extractText(from pdfURL: URL, pages: [Int]) -> String {
        guard !pages.isEmpty, let document = PDFDocument(url: pdfURL) else { return "" }
        var parts: [String] = []
        let pageCount = document.pageCount
        for pageNumber in pages {
            let index = pageNumber - 1   // PDFDocument is 0-indexed
            guard index >= 0 && index < pageCount else { continue }
            if let page = document.page(at: index), let text = page.string, !text.isEmpty {
                parts.append(text)
            }
        }
        return parts.joined(separator: "\n\n")
    }
}
