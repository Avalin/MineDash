namespace MineDash.Services;

public interface ITimeDisplayService
{
    string FormatTime(DateTime timestamp);

    DateTime NormalizeForSort(DateTime timestamp);
}
