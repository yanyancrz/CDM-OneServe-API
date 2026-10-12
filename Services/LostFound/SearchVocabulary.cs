namespace CDM_OneServe_API.Services.LostFound;

/// <summary>
/// Search vocabulary for the Lost &amp; Found board.
///
/// WHY A HAND-KEPT DICTIONARY
/// The board is searched by students looking for "cellphone", "bag" or
/// "ID" - words that rarely match a title exactly. A small, curated map
/// of related words covers what a campus actually loses without a search
/// engine, a paid AI API or a new dependency. Keep it small: every extra
/// word is a match that has to stay relevant, and a bloated dictionary
/// is how "wallet" starts matching "water bottle".
///
/// HOW IT IS USED
/// <see cref="Expand"/> turns one typed keyword into the keyword itself
/// plus its known relatives. The board matches reports against ANY of
/// those words, and scores an exact-word match higher than a synonym
/// match, so relevance is preserved (see GetPublicAsync).
///
/// MAINTENANCE
/// Keys and values are lowercase. Add a group when students repeatedly
/// report items the board cannot find. Nothing here is user data.
/// </summary>
public static class SearchVocabulary
{
    /// <summary>
    /// Related-word groups. A group is a SET of words a student would
    /// reasonably type for the same kind of item; any word in a group
    /// pulls in the rest of it (only the words actually listed, so "bag"
    /// does not silently pull in "luggage" - add it on purpose).
    /// </summary>
    private static readonly Dictionary<string, string[]> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        // --- phones ---
        ["phone"] = new[] { "cellphone", "cell phone", "mobile", "mobile phone", "smartphone", "telephone" },
        ["cellphone"] = new[] { "phone", "cell phone", "mobile", "mobile phone", "smartphone", "telephone" },
        ["mobile"] = new[] { "phone", "cellphone", "smartphone", "cell phone" },
        ["smartphone"] = new[] { "phone", "cellphone", "mobile phone", "mobile" },
        ["telephone"] = new[] { "phone", "cellphone", "mobile" },

        // --- carry ---
        ["bag"] = new[] { "backpack", "school bag", "knapsack", "sack", "tote", "pouch", "purse" },
        ["backpack"] = new[] { "bag", "school bag", "knapsack" },
        ["knapsack"] = new[] { "backpack", "bag", "school bag" },
        ["purse"] = new[] { "wallet", "bag", "pouch" },

        // --- money & cards ---
        ["wallet"] = new[] { "purse", "billfold", "money holder" },
        ["id"] = new[] { "identification card", "school id", "student id", "id card", "school id card", "corridor" },
        ["ids"] = new[] { "identification card", "school id", "student id", "id card" },

        // --- everyday campus items ---
        ["umbrella"] = new[] { "umbrellas", "folding umbrella" },
        ["bottle"] = new[] { "tumbler", "water bottle", "container", "flask" },
        ["tumbler"] = new[] { "bottle", "water bottle", "container" },
        ["keys"] = new[] { "key", "keychain", "key ring", "bunch of keys" },
        ["key"] = new[] { "keys", "keychain", "key ring" },
        ["charger"] = new[] { "charging cable", "adapter", "charging brick", "power bank" },
        ["laptop"] = new[] { "notebook computer", "notebook pc", "macbook" },
        ["notebook"] = new[] { "laptop", "journal", "binder" },
        ["book"] = new[] { "books", "textbook", "module", "notebook" },
        ["eyeglasses"] = new[] { "glasses", "spectacles", "shades", "sunglasses" },
        ["glasses"] = new[] { "eyeglasses", "spectacles", "sunglasses", "shades" },
        ["jacket"] = new[] { "coat", "hoodie", "sweater", "uniform", "blouse" },
        ["uniform"] = new[] { "jacket", "pe attire", "ppe", "clothes" },
        ["shoes"] = new[] { "shoe", "sneakers", "rubber shoes", "footwear" },
        ["shoe"] = new[] { "shoes", "sneakers", "rubber shoes" },
        ["jewelry"] = new[] { "necklace", "bracelet", "ring", "earrings", "watch" },
        ["watch"] = new[] { "wristwatch", "smartwatch", "jewelry" },
        ["earphones"] = new[] { "earbuds", "headset", "headphones", "airpods" },
        ["headphones"] = new[] { "earphones", "headset", "earbuds" },
    };

    /// <summary>
    /// Words that are too generic to search on their own: matching them
    /// would pull in half the board. Dropped when they are the whole
    /// search (a multi-word search keeps its meaning).
    /// </summary>
    private static readonly HashSet<string> TooGeneric = new(StringComparer.OrdinalIgnoreCase)
    {
        "item", "items", "thing", "things", "stuff", "lost", "found", "the", "a", "an", "my", "me",
    };

    /// <summary>
    /// The keyword plus every related word the dictionary knows,
    /// keyword first, de-duplicated, capped so a pathological entry
    /// cannot spray the query with patterns.
    /// </summary>
    public static List<string> Expand(string? keyword)
    {
        var normalized = Normalize(keyword);
        if (normalized.Length == 0) return new List<string>();

        var terms = new List<string> { normalized };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { normalized };

        foreach (var word in Groups.Keys)
        {
            // A group applies when the keyword IS the group key or is one
            // of its listed relatives ("phone" -> "mobile" group too).
            var related = keyword!.Trim().ToLowerInvariant();
            var applies = word.Equals(related, StringComparison.OrdinalIgnoreCase) ||
                          (Groups.TryGetValue(word, out var list) &&
                           list.Any(w => w.Equals(related, StringComparison.OrdinalIgnoreCase)));

            if (!applies) continue;

            foreach (var candidate in new[] { word }.Concat(Groups[word]))
            {
                if (candidate.Length == 0) continue;
                if (TooGeneric.Contains(candidate)) continue;
                if (seen.Add(candidate)) terms.Add(candidate);
                if (terms.Count >= MaxTerms) return terms;
            }
        }

        return terms;
    }

    /// <summary>Hard cap on the words one search expands to.</summary>
    public const int MaxTerms = 8;

    /// <summary>
    /// Trims and strips the SQL LIKE wildcards (% and _), so a typed "%"
    /// turns into a literal character instead of matching everything.
    /// Collapses inner whitespace so "cell  phone" finds "cell phone".
    /// </summary>
    public static string Normalize(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return string.Empty;

        var cleaned = new string(keyword
            .Trim()
            .Where(c => c != '%' && c != '_')
            .ToArray());

        return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
