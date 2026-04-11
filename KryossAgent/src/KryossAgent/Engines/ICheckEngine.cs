using KryossAgent.Models;

namespace KryossAgent.Engines;

/// <summary>
/// Interface for all check engines. Each engine handles one check type.
/// Engines are "dumb" — they read raw values and return them.
/// No PASS/FAIL evaluation happens in the agent.
/// </summary>
public interface ICheckEngine
{
    string Type { get; }
    List<CheckResult> Execute(IReadOnlyList<ControlDef> controls);
}
