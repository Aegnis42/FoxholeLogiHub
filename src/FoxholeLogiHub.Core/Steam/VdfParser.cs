using System.Text;

namespace FoxholeLogiHub.Core.Steam;

/// <summary>Un nœud d'un document VDF (format texte clé-valeur de Valve) : soit une valeur, soit des enfants.</summary>
public sealed class VdfNode
{
    public string? Value { get; set; }
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Accès enfant par clé (insensible à la casse), null si absent.</summary>
    public VdfNode? this[string key] => Children.TryGetValue(key, out VdfNode? n) ? n : null;
}

/// <summary>
/// Parseur minimal du format VDF/KeyValues de Valve (loginusers.vdf, libraryfolders.vdf…).
/// Gère les chaînes entre guillemets (avec échappements), les blocs { } et les commentaires //.
/// </summary>
public static class VdfParser
{
    public static VdfNode Parse(string text)
    {
        List<string> tokens = Tokenize(text);
        int i = 0;
        return ParseObject(tokens, ref i);
    }

    private static VdfNode ParseObject(List<string> tokens, ref int i)
    {
        var node = new VdfNode();
        while (i < tokens.Count)
        {
            string token = tokens[i];
            if (token == "}")
            {
                i++;
                break;
            }

            string key = token;
            i++;
            if (i >= tokens.Count)
                break;

            string next = tokens[i];
            if (next == "{")
            {
                i++;
                node.Children[key] = ParseObject(tokens, ref i);
            }
            else
            {
                i++;
                node.Children[key] = new VdfNode { Value = next };
            }
        }
        return node;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '{' || c == '}') { tokens.Add(c.ToString()); i++; continue; }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        sb.Append(text[i] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            var x => x,
                        });
                    }
                    else
                    {
                        sb.Append(text[i]);
                    }
                    i++;
                }
                i++; // guillemet fermant
                tokens.Add(sb.ToString());
                continue;
            }

            // Jeton non quoté
            var word = new StringBuilder();
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}')
            {
                word.Append(text[i]);
                i++;
            }
            tokens.Add(word.ToString());
        }
        return tokens;
    }
}
