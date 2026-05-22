using ITService.Backup.Models;

namespace ITService.Backup.Services;

public sealed class BackupProgressTracker
{
    private readonly List<PlannedStep> _steps = [];
    private readonly List<string> _completed = [];
    private int _currentIndex = -1;
    private int _subPercent;
    private string _currentAction = "Подготовка...";

    public void PlanStep(string name, long weightBytes) =>
        _steps.Add(new PlannedStep(name, Math.Max(weightBytes, 1)));

    public void BeginStep(int index, string action)
    {
        _currentIndex = index;
        _subPercent = 0;
        _currentAction = action;
    }

    public void SetSubProgress(int percent, string? action = null)
    {
        _subPercent = Math.Clamp(percent, 0, 100);
        if (action != null)
            _currentAction = action;
    }

    public void EndStep()
    {
        if (_currentIndex >= 0 && _currentIndex < _steps.Count)
            _completed.Add($"✓ {_steps[_currentIndex].Name}");
        _currentIndex = -1;
        _subPercent = 0;
    }

    public BackupProgressReport Snapshot()
    {
        var total = _steps.Sum(s => s.Weight);
        if (total <= 0)
            return BackupProgressReport.Create(0, _currentAction, _completed);

        long done = 0;
        for (var i = 0; i < _completed.Count && i < _steps.Count; i++)
            done += _steps[i].Weight;

        long currentPart = 0;
        if (_currentIndex >= 0 && _currentIndex < _steps.Count)
            currentPart = _steps[_currentIndex].Weight * _subPercent / 100;

        var percent = (int)Math.Round((done + currentPart) * 100.0 / total);
        return BackupProgressReport.Create(Math.Min(percent, 99), _currentAction, _completed);
    }

    public BackupProgressReport Finished(bool success, string message) => new()
    {
        Percent = 100,
        CurrentAction = message,
        CompletedActions = _completed,
        IsFinished = true,
        IsSuccess = success,
        ErrorMessage = success ? null : message
    };

    private sealed record PlannedStep(string Name, long Weight);
}
