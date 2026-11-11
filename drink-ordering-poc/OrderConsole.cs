namespace drink_ordering_poc;

public sealed class OrderConsole(TextReader input, TextWriter output)
{
    public int ReadChoice(string prompt, IReadOnlyList<string> choices, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine(prompt);
            for (int i = 0; i < choices.Count; i++)
                output.WriteLine($"{i + 1}. {choices[i]}");
            string line = ReadLine(cancellationToken);
            if (int.TryParse(line, out int choice) && choice >= 1 && choice <= choices.Count)
                return choice;

            output.WriteLine($"Enter a number from 1 to {choices.Count}.");
        }
    }

    public int ReadNonNegativeInt(string prompt, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine(prompt);
            string line = ReadLine(cancellationToken);
            if (int.TryParse(line, out int count) && count >= 0)
                return count;

            output.WriteLine($"Enter a non-negative whole number (0 to {int.MaxValue}).");
        }
    }

    public void WriteLine(string message) => output.WriteLine(message);

    private string ReadLine(CancellationToken cancellationToken)
    {
        output.Write("> ");
        string line = input.ReadLine() ?? throw new EndOfStreamException("Input closed.");
        cancellationToken.ThrowIfCancellationRequested();
        return line;
    }
}
