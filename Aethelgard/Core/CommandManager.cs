using System.Collections.Generic;

namespace Aethelgard.Core
{
    /// <summary>
    /// Manages the execution and history of Commands.
    /// Provides Undo functionality.
    /// </summary>
    public class CommandManager
    {
        private readonly Stack<ICommand> _undoStack = new Stack<ICommand>();
        // Future: Stack<ICommand> _redoStack;

        // Simple Singleton pattern for now (as per Style Guide "Singleton Managers")
        private static CommandManager? _instance;
        public static CommandManager Instance => _instance ??= new CommandManager();

        public void ExecuteCommand(ICommand command)
        {
            command.Execute();
            _undoStack.Push(command);
        }

        public void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var command = _undoStack.Pop();
                command.Undo();
                // Future: Push to redo stack
            }
        }
    }
}
