using System;

namespace configurable_number_pipeline_poc;

// Simplistic example of dependency that can be passed in to operations via context.
public class UserInterfaceProvider
{
    public int PromptUserForIntInput(string message)
    {
        Console.WriteLine(message);
        
        int intInput;
        while (!int.TryParse(Console.ReadLine(), out intInput))
        {
            Console.WriteLine("Input must be an integer.");
        }
        return intInput;
    }

    public void InformUser(string message) => Console.WriteLine(message);
}
