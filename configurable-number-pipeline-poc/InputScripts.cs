using System;
using System.Collections.Immutable;
using operator_result_management;
using operator_result_management.Basic;

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
        Console.WriteLine();
        
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

    public static ImmutableList<IInstruction> GetInstructionSelection(IProcess currentProcess)
    {
        List<int> selectedIndicies = [];
        OutputExistingProcess(currentProcess);

        int input;
        do
        {
            input = GetValidIntInput(
                $"Select next input instruction or 0 to complete (note: empty instruction input is valid). \r\n Selected:[{string.Join(',', selectedIndicies)}]",
                [.. Enumerable.Range(0, currentProcess.Instructions.Count + 1).Select(i => i)]);
            if (input != 0)
                selectedIndicies.Add(input);
        }
        while (input != 0);

        return [..selectedIndicies.Select(i => currentProcess.Instructions[i - 1])];
    }

    private static int GetValidIntInput(string message, HashSet<int> validOptions)
    {
        int input;
        Console.WriteLine(message);
        while (!int.TryParse(Console.ReadLine(), out input) || !validOptions.Contains(input))
        {
            Console.WriteLine($"Input must be one of [{string.Join(',', validOptions)}].");
        }
        return input;
    }

    public static IEnumerable<IInstruction<TContext>> SelectBinding<TContext>(
        ImmutableList<BindingResponse<TContext>> responses, IProcess process)
        where TContext : IExecutionContext
    {
        var readyResponses = responses.OfType<ReadyResponse<TContext>>().ToList();
        if (readyResponses.Count == 0)
        {
            Console.WriteLine("No valid instructions for selection.");
            return [];
        }

        var instructions = process.Instructions;
        var instructionNumbers = new Dictionary<IInstruction, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < instructions.Count; i++)
        {
            instructionNumbers.Add(instructions[i], i + 1);
        }

        Console.WriteLine("Eligible instructions for creation:");
        for (var i = 0; i < readyResponses.Count; i++)
        {
            Console.WriteLine($"{i+1}) {readyResponses[i].BoundInstructionType.Name}");
            foreach(var factoryBinding in readyResponses[i].Bindings)
            {
                Console.WriteLine($"{factoryBinding.ParameterName}: {instructionNumbers[factoryBinding.BoundInstruction]}) {factoryBinding.BoundInstruction.GetType().Name}");
            }
        }
        
        Console.WriteLine();
        var input = GetValidIntInput("Select instruction to create:", [..Enumerable.Range(1, readyResponses.Count)]);
        return [readyResponses[input - 1].Create()];
    }
}

public enum ConfigurePipelineUserAction
{
    Edit,
    Execute
}
