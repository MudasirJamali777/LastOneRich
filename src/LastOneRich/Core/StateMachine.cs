namespace LastOneRich.Core;

public interface IGameState
{
    void Enter();
    void Exit();
    void Update(float dt);
    void Draw();
}

/// <summary>Simple state machine (GDD 18.1). Replace() defers to end-of-update to stay reentrancy-safe.</summary>
public sealed class StateMachine
{
    IGameState _current;
    IGameState _pending;

    public bool IsEmpty => _current == null && _pending == null;
    public IGameState Current => _current;

    public void Replace(IGameState next)
    {
        _pending = next;
        // If nothing is running (e.g. first state), apply immediately.
        if (_current == null) ApplyPending();
    }

    public void Update(float dt)
    {
        _current?.Update(dt);
        ApplyPending();
    }

    void ApplyPending()
    {
        if (_pending == null) return;
        _current?.Exit();
        _current = _pending;
        _pending = null;
        _current.Enter();
    }

    public void Draw() => _current?.Draw();

    /// <summary>Unwind everything — the main loop exits when the machine is empty.</summary>
    public void Quit()
    {
        _current?.Exit();
        _current = null;
        _pending = null;
    }
}
