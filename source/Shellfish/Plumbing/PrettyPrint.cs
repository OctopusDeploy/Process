using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Octopus.Shellfish.Plumbing;

// Copied so we don't need to add another dependency
static class ExceptionExtensions
{
    public static string PrettyPrint(this Exception ex, bool printStackTrace = true)
    {
        var sb = new StringBuilder();
        PrettyPrint(sb, ex, printStackTrace);
        return sb.ToString().Trim();
    }

    static void PrettyPrint(StringBuilder sb, Exception ex, bool printStackTrace)
    {
        if (ex is AggregateException aex)
        {
            AppendAggregateException(sb, printStackTrace, aex);
            return;
        }

        if (ex is OperationCanceledException)
        {
            sb.AppendLine("The task was canceled");
            return;
        }

        if (ex.GetType().Name == "SqlException")
        {
            var number = ex.GetType().GetRuntimeProperty("Number")?.GetValue(ex);
            sb.AppendLine($"SQL Error {number} - {ex.Message}");
        }
        else
        {
            sb.AppendLine(ex.Message);
        }

        if (ex.GetType().Name == "ControlledFailureException")
            return;

        if (printStackTrace)
            AddStackTrace(sb, ex);

        if (ex.InnerException == null)
            return;

        if (printStackTrace)
            sb.AppendLine("--Inner Exception--");

        PrettyPrint(sb, ex.InnerException, printStackTrace);
    }

    static void AppendAggregateException(StringBuilder sb, bool printStackTrace, AggregateException aex)
    {
        if (!printStackTrace && aex.InnerExceptions.Count == 1 && aex.InnerException != null)
        {
            PrettyPrint(sb, aex.InnerException, printStackTrace);
        }
        else
        {
            sb.AppendLine("Aggregate Exception");
            if (printStackTrace)
                AddStackTrace(sb, aex);
            for (var x = 0; x < aex.InnerExceptions.Count; x++)
            {
                sb.AppendLine($"--Inner Exception {x + 1}--");
                PrettyPrint(sb, aex.InnerExceptions[x], printStackTrace);
            }
        }
    }

    static void AddStackTrace(StringBuilder sb, Exception ex)
    {
        if (ex is ReflectionTypeLoadException rtle)
            AddReflectionTypeLoadExceptionDetails(rtle, sb);

        sb.AppendLine(ex.GetType().FullName);
        try
        {
            sb.AppendLine(StackTraceHelper.GetCleanStackTrace(ex));
        }
        catch // Sometimes fails printing the trace
        {
            sb.AppendLine(ex.StackTrace);
        }
    }

    static void AddReflectionTypeLoadExceptionDetails(ReflectionTypeLoadException rtle, StringBuilder sb)
    {
        if (rtle.LoaderExceptions == null)
            return;

        foreach (var loaderException in rtle.LoaderExceptions)
        {
            if (loaderException == null)
                continue;

            sb.AppendLine();
            sb.AppendLine("--Loader Exception--");
            PrettyPrint(sb, loaderException, true);

            var fusionLog = (loaderException as FileNotFoundException)?.FusionLog;
            if (!string.IsNullOrEmpty(fusionLog))
                sb.Append("Fusion log: ").AppendLine(fusionLog);
        }
    }

    static class StackTraceHelper
    {
        //VBConversions Note: Former VB static variables moved to class level because they aren't supported in C#.
        static readonly Regex Re1 = new("VB\\$StateMachine_[\\d]+_(.+)\\.MoveNext\\(\\)");
        static readonly Regex Re2 = new("<([^>]+)>[^.]+\\.MoveNext\\(\\)");
        static readonly Regex Re3 = new("^(.*) in (.*):line ([0-9]+)$");

        public static string GetCleanStackTrace(Exception ex)
        {
            if (ex.StackTrace == null)
                return "";

            var sb = new StringBuilder();

            foreach (var stackTrace in ex.StackTrace.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = stackTrace;

                // Get rid of stack-frames that are part of the BCL async machinery
                if (s.StartsWith("   at "))
                    s = s.Substring(6);
                else
                    continue;

                if (s == "System.Runtime.CompilerServices.TaskAwaiter.ThrowForNonSuccess(Task task)")
                    continue;

                if (s == "System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification(Task task)")
                    continue;

                if (s == "System.Runtime.CompilerServices.TaskAwaiter`1.GetResult()")
                    continue;

                if (s == "System.Runtime.CompilerServices.TaskAwaiter.GetResult()")
                    continue;

                // Get rid of stack-frames that are part of the runtime exception handling machinery
                if (s == "System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw()")
                    continue;

                // Get rid of stack-frames that are part of .NET Native machiner
                if (s.Contains("!<BaseAddress>+0x"))
                    continue;

                // Get rid of stack frames from VB and C# compiler-generated async state machine
                s = Re1.Replace(s, "$1");
                s = Re2.Replace(s, "$1");

                // If the stack trace had PDBs, "Alpha.Beta.GammaAsync in c:\code\module1.vb:line 53"
                var re3Match = Re3.Match(s);
                s = re3Match.Success ? re3Match.Groups[1].Value : s;
                var pdbfile = re3Match.Success ? re3Match.Groups[2].Value : null;
                var pdbline = re3Match.Success ? (int?)int.Parse(Convert.ToString(re3Match.Groups[3].Value)) : null;

                // Get rid of stack frames from AsyncStackTrace
                if (s.EndsWith("AsyncStackTraceExtensions.Log`1"))
                    continue;

                if (s.EndsWith("AsyncStackTraceExtensions.Log"))
                    continue;

                if (s.Contains("AsyncStackTraceExtensions.Log<"))
                    continue;

                var fullyQualified = s;

                if (pdbfile != null)
                    sb.AppendFormat("   at {1} in {2}:line {3}{0}",
                        Environment.NewLine,
                        fullyQualified,
                        Path.GetFileName(pdbfile),
                        pdbline);
                else
                    sb.AppendFormat("   at {1}{0}", "\r\n", fullyQualified);
            }

            return sb.ToString();
        }
    }
}