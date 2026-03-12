namespace AGXUnity_Excavator.Scripts.Control.Core
{
  public interface IOperatorCommandSource
  {
    string SourceName { get; }

    OperatorCommand ReadCommand();
  }
}
