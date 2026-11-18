using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCIsland.Core;

public static partial class Brain
{
    public static event Action<string> Status;
    public static void ReportStatus(string s) => Status?.Invoke(s);
    public static string LastIcon;
    public static event Action StreamModeChanged;
    static readonly AsyncLocal<string> currentInput = new();
    static readonly AsyncLocal<bool> noAi = new();

    static bool AiAllowedNow => Ai.Ready && !noAi.Value && currentInput.Value != null;

    static Reply AiOr(Reply fallback) => AiAllowedNow ? new Deferred(() => AskAi(currentInput.Value, fallback)) : fallback;

    static async Task<Reply> AnswerQuestion(string input)
    {
        bool useGpu = AiModels.HasGraphicsCard;
        if (Native.IsFullscreenAppRunning())
        {
            if (Settings.Current.AiGameMode == "off") return new Message("AI is off while you play", false);
            if (Settings.Current.AiGameMode == "cpu") useGpu = false;
        }
        Status?.Invoke("Thinking");
        var text = await Ai.Answer(input, useGpu);
        if (string.IsNullOrWhiteSpace(text)) return new Message("The AI couldn't answer that", false);
        return new Answer(text, text);
    }

    static async Task<Reply> AskAi(string input, Reply fallback)
    {
        bool game = Native.IsFullscreenAppRunning();
        bool useGpu = true;
        if (game)
        {
            if (Settings.Current.AiGameMode == "off") return new Message("AI is off while you play. Try a shorter command", false);
            if (Settings.Current.AiGameMode == "cpu") useGpu = false;
        }
        if (!AiModels.HasGraphicsCard) useGpu = false;

        var cmds = Ai.Cached(input);
        bool fromCache = cmds != null;
        if (cmds == null)
        {
            Status?.Invoke("Thinking");
            var ans = await Ai.Ask(input, useGpu);
            if (ans.Error != null) return fallback ?? new Message(ans.Error, false);
            if (ans.Commands.Count == 0)
                return string.IsNullOrWhiteSpace(ans.Say) ? (fallback ?? new Message("I couldn't work that out", false)) : new Message(ans.Say.Trim());
            cmds = ans.Commands;
        }

        cmds = cmds.Where(c => Text.Clean(c) != Text.Clean(input)).ToList();
        if (cmds.Count == 0) return fallback ?? new Message("I couldn't work that out", false);

        if (cmds.Count == 1)
        {
            noAi.Value = true;
            var r = await Handle(cmds[0]);
            if (r is Message m)
            {
                if (m.Ok && m.Text.Length > 0 && !fromCache) Ai.Remember(input, cmds);
                return m.Ok ? new Message($"{m.Text} · AI") : m;
            }
            if (!fromCache) Ai.Remember(input, cmds);
            return r;
        }

        bool risky = cmds.Any(c => Parser.Parse(c).Kind is Kind.Close or Kind.Delete or Kind.Shutdown or Kind.Restart or Kind.Sleep or Kind.Tabs);
        Func<Task<Reply>> runAll = async () =>
        {
            noAi.Value = true;
            var errors = new List<string>();
            foreach (var c in cmds)
            {
                var r = await Handle(c, insideShortcut: true);
                if (r is Confirm cf) r = await cf.OnYes();
                if (r is Deferred d) r = await d.Run();
                if (r is Pick p && p.Choices.Count > 0) r = await p.Choices[0].Run();
                if (r is Message mm && !mm.Ok && mm.Text.Length > 0) errors.Add(mm.Text);
                await Task.Delay(150);
            }
            if (errors.Count == 0 && !fromCache) Ai.Remember(input, cmds);
            return errors.Count == 0 ? new Message($"Done: {string.Join(", ", cmds)} · AI") : new Message(errors[0], false);
        };
        string list = string.Join(" · ", cmds);
        return new Confirm(risky ? "Do all of this?" : "Do this?", list, "Do it", risky, runAll);
    }
}
