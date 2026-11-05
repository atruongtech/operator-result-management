using System;

namespace configurable_number_pipeline_poc;

// Simplistic example of dependency that can be passed in to operations via context.
public class UserInterfaceProvider
{
    public int PromptUserForIntInput(string message)
    {
        Console.WriteLine(message);
        if (int.TryParse(Console.ReadLine(), out var input))
            return input;

        throw new InvalidDataException("Could not convert input to integer.");
    }

    public void InformUser(string message) => Console.WriteLine(message);
}
