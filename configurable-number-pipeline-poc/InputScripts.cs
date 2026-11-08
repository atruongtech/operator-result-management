using System;
using operator_result_management;

namespace configurable_number_pipeline_poc;

public class InputScripts
{
    public static void OutputExistingProcess(IProcess process)
    {
        var counter = 0;

        foreach(var instruction in process.Instructions)
        {
            counter++;
            Console.WriteLine($"{counter}) {instruction.GetType().Name}");
        }
    }

    public static ConfigurePipelineUserAction PromptUserForAction()
    {
        Console.WriteLine("Choose an action:");
        Console.WriteLine("1) Edit");
        Console.WriteLine("2) Execute current");
        
        int intInput;
        while (!int.TryParse(Console.ReadLine(), out intInput) || (intInput != 1 && intInput != 2))
        {
            Console.WriteLine("Input must be 1 or 2");
        }
        return intInput switch
        {
            1 => ConfigurePipelineUserAction.Edit,
            2 => ConfigurePipelineUserAction.Execute,
            _ => throw new InvalidOperationException("Unexpected input")
        };
    }
}

public enum ConfigurePipelineUserAction
{
    Edit,
    Execute
}
