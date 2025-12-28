namespace Aethelgard.Core
{
    /// <summary>
    /// Interface for the Command Pattern.
    /// Encapsulates an action that can be executed and undone.
    /// </summary>
    public interface ICommand
    {
        void Execute();
        void Undo();
    }
}
