using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using QuickMatch.Localization;
using QuickMatch.Matchmaking;

namespace QuickMatch.Ui;

// Yes/no questions shown with the game's own confirmation popup. The answer that keeps the run or the search
// going is always the popup's Yes button: Save and Wait, Host the Run, Keep Looking.
internal static class RegroupPrompt
{
    // Host, mid-run, left alone: true for Save and Wait (save, quit and reopen the run now), false for
    // Continue Solo.
    public static Task<bool> AskRegroupAsync(string playerName)
    {
        var body = new LocString(LocSetup.Table, "QUICKMATCH.REGROUP.body");
        // The popup's text is BBCode: a Steam name must not be able to add tags of its own.
        body.Add("player", EscapeBbcode(playerName));
        return AskAsync(body, "QUICKMATCH.REGROUP.header", "QUICKMATCH.REGROUP.no", "QUICKMATCH.REGROUP.yes");
    }

    // A client of a run whose host left, at its turn in the order: true for Host the Run, false for Pass.
    // TakeOverQuestion.Close takes the question away unanswered (its time is up, or the run's lobby was
    // found), which answers Pass.
    public static TakeOverQuestion AskTakeOver(string hostName, double seconds = MigrationPolicy.AnswerSeconds, bool hasNext = false)
    {
        var body = new LocString(LocSetup.Table, "QUICKMATCH.TAKE_OVER.body");
        body.Add("host", EscapeBbcode(hostName));
        NGenericPopup? popup = NGenericPopup.Create();
        if (popup == null || !Show(popup, "take-over question"))
        {
            MainFile.Logger.Warn("the take-over question cannot be shown here");
            return new TakeOverQuestion(null, Task.FromResult(false));
        }
        Task<bool> answer = popup.WaitForConfirmation(body, new LocString(LocSetup.Table, "QUICKMATCH.TAKE_OVER.header"),
            new LocString(LocSetup.Table, "QUICKMATCH.TAKE_OVER.no"), new LocString(LocSetup.Table, "QUICKMATCH.TAKE_OVER.yes"));
        var question = new TakeOverQuestion(popup, answer);
        _ = question.CountDownAsync(Loc.Get("QUICKMATCH.TAKE_OVER.header"), body.GetFormattedText(), seconds, hasNext);
        return question;
    }

    // The rejoin search found nothing for a while. True for Keep Looking, false for Abandon Run (the run is
    // forgotten).
    public static Task<bool> AskKeepWaitingAsync()
    {
        return AskAsync(new LocString(LocSetup.Table, "QUICKMATCH.REJOIN_TIMEOUT.body"),
            "QUICKMATCH.REJOIN_TIMEOUT.header", "QUICKMATCH.REJOIN_TIMEOUT.no", "QUICKMATCH.REJOIN_TIMEOUT.yes");
    }

    // Square brackets as BBCode's [lb] and [rb], so the text shows as it is.
    public static string EscapeBbcode(string text)
    {
        var escaped = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            escaped.Append(c switch { '[' => "[lb]", ']' => "[rb]", _ => c.ToString() });
        }
        return escaped.ToString();
    }

    private static Task<bool> AskAsync(LocString body, string header, string no, string yes)
    {
        NGenericPopup? popup = NGenericPopup.Create();
        if (popup == null || !Show(popup, header))
        {
            return Task.FromResult(false);
        }
        return popup.WaitForConfirmation(body, new LocString(LocSetup.Table, header), new LocString(LocSetup.Table, no), new LocString(LocSetup.Table, yes));
    }

    // The game's modal container shows one window at a time and refuses another while one is open, and a
    // refused popup never gets its nodes, so the window open, real or left behind, is closed first.
    private static bool Show(NGenericPopup popup, string what)
    {
        if (NModalContainer.Instance is not { } modals)
        {
            return false;
        }
        if (modals.OpenModal is { } open)
        {
            MainFile.Logger.Warn($"the {what} waits for another window to close ({open.GetType().Name}); closing that one");
            modals.Clear();
        }
        modals.Add(popup);
        return true;
    }
}

// The take-over question while it is open; closing it clears the window.
internal sealed class TakeOverQuestion(NGenericPopup? popup, Task<bool> answer)
{
    private static readonly MethodInfo? NoPressed =
        typeof(NGenericPopup).GetMethod("OnNoButtonPressed", BindingFlags.Instance | BindingFlags.NonPublic);

    public Task<bool> Answer { get; } = answer;

    // True for Host the Run, false for Pass; null while the question is open.
    public bool? Given => Answer.IsCompletedSuccessfully ? Answer.Result : Answer.IsCompleted ? false : null;

    // Once a second, until it is answered or taken away. The rejoin search closes the question itself when
    // the time is up.
    public async Task CountDownAsync(string header, string body, double seconds, bool hasNext)
    {
        double until = Time.GetTicksMsec() / 1000.0 + seconds;
        try
        {
            while (!Answer.IsCompleted && popup != null && GodotObject.IsInstanceValid(popup))
            {
                string time = Loc.With(hasNext ? "QUICKMATCH.TAKE_OVER.time_next" : "QUICKMATCH.TAKE_OVER.time",
                    "time", MigrationPolicy.Countdown(until - Time.GetTicksMsec() / 1000.0));
                popup.GetNodeOrNull<NVerticalPopup>("VerticalPopup")?.SetText(header, $"{body}\n\n[gold]{time}[/gold]");
                await GodotDelay.Seconds(1, CancellationToken.None);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not show the time left to answer: {e.Message}");
        }
    }

    // Takes the question away as Pass would, if it is still open.
    public void Close()
    {
        if (Answer.IsCompleted || popup == null || !GodotObject.IsInstanceValid(popup))
        {
            return;
        }
        try
        {
            NoPressed?.Invoke(popup, [null]);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"could not close the take-over question: {e.Message}");
            popup.QueueFree();
        }
        // Pressing No also closes the window around the popup (the game's NVerticalPopup.Close); without it the screen stays
        // dimmed, takes no clicks, and the container refuses every later question.
        if (NModalContainer.Instance is { } modals && ReferenceEquals(modals.OpenModal, popup))
        {
            modals.Clear();
        }
    }
}
