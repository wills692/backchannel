namespace BackChannel.App.Commands;

public sealed record CommandLine(
    bool IsCommand,
    string Name,
    string Arguments,
    string Text)
{
    public static CommandLine Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var text = input.Trim();
        if (!text.StartsWith('/'))
        {
            return new CommandLine(false, string.Empty, string.Empty, text);
        }

        var separator = text.IndexOf(' ');
        var name = separator < 0
            ? text[1..]
            : text[1..separator];
        var arguments = separator < 0
            ? string.Empty
            : text[(separator + 1)..].Trim();

        return new CommandLine(
            true,
            name.ToLowerInvariant(),
            arguments,
            text);
    }
}
