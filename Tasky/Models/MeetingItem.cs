namespace Tasky.Models;

public class MeetingItem
{
    public string   Id            { get; init; } = "";
    public string   Subject       { get; init; } = "(No title)";
    public DateTime Start         { get; init; }
    public DateTime End           { get; init; }
    public string?  JoinUrl       { get; init; }
    public string   OrganizerName { get; init; } = "";

    public bool   IsTeamsMeeting => JoinUrl != null;

    public string TimeRange => $"{Start:h:mm tt} – {End:h:mm tt}";

    public string DurationLabel
    {
        get
        {
            var d = End - Start;
            if (d.TotalHours >= 1) return $"{(int)d.TotalHours}h {d.Minutes}m";
            return $"{(int)d.TotalMinutes}m";
        }
    }
}
