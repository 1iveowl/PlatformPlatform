namespace Account.Emails.Layout;

// A localized sentence that carries markup around one of its parts, such as the link in the invitation, cannot be
// composed by concatenating fragments: the parts sit in a different order in another language. The resource therefore
// holds the whole sentence with {0}-style placeholders, and this splits it into the literal runs and the placeholder
// positions so a component can render the literals as text and each placeholder as its own markup.
public static class EmailTextFormat
{
    public sealed record Segment(string Literal, int ArgumentIndex)
    {
        public bool IsArgument => ArgumentIndex >= 0;
    }

    public static Segment[] Split(string format)
    {
        var segments = new List<Segment>();
        var literal = new System.Text.StringBuilder();

        for (var position = 0; position < format.Length; position++)
        {
            var character = format[position];
            if (character != '{')
            {
                literal.Append(character);
                continue;
            }

            var closingBrace = format.IndexOf('}', position);
            if (closingBrace < 0 || !int.TryParse(format.AsSpan(position + 1, closingBrace - position - 1), out var argumentIndex))
            {
                throw new FormatException($"The email text '{format}' has a '{{' that does not open a numbered placeholder.");
            }

            if (literal.Length > 0)
            {
                segments.Add(new Segment(literal.ToString(), -1));
                literal.Clear();
            }

            segments.Add(new Segment(string.Empty, argumentIndex));
            position = closingBrace;
        }

        if (literal.Length > 0) segments.Add(new Segment(literal.ToString(), -1));

        return segments.ToArray();
    }
}
