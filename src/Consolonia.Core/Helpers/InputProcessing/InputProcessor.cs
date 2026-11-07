#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Avalonia.Logging;
using Consolonia.Core.Helpers.Logging;

namespace Consolonia.Core.Helpers.InputProcessing
{
    // todo: low: $100 prize for showing me how to significantly simplify this processor (possible with IMatcher contract)
    // in a way I don't have to spend several hours every time to just understand how it works
    // $50 for partial satisfaction
    
    public class InputProcessor<T>(IEnumerable<IMatcher<T>> matchers)
    {
        private readonly ParametrizedLogger _infoLogger = Log.CreateInputLogger(LogEventLevel.Information);
        private readonly ParametrizedLogger _traceLogger = Log.CreateInputLogger(LogEventLevel.Verbose);

        private int _previousTopMatcherIndex = -1;
        private ImmutableArray<IMatcher<T>> Matchers { get; } = [..matchers];

        public void ProcessChunk(IReadOnlyCollection<T> chunk)
        {
            _traceLogger.Log2("Processing input chunk with {Count} items...", chunk.Count);

            foreach (T input in chunk)
                ProcessSingleInput(input);

            _infoLogger.Log2("Trying to flush starting from index 0");
            TryFlushStartingFrom(0);
            LogMatchersState();
        }
        
        private void ProcessSingleInput(T input, bool canRetry = true /*solving "AAB" input for {"AB, "A"} matchers*/)
        {
            _infoLogger.Log2("Processing single input: {Input}", input);
            int currentTopMatcherIndex = Matchers.Length;


            StringBuilder logAllSb = new();
            logAllSb.AppendLine();
            logAllSb.AppendLine("-----------------------------------------------------");
            for (int i = 0; i < Matchers.Length; i++)
            {
                StringBuilder logSb = new();
                try
                {
                    IMatcher<T> matcher = Matchers[i];

                    _traceLogger.Log3("Processing matcher {MatcherIndex}: {Matcher}", i, matcher.GetDebugInfo());
                    logSb.AppendJoin(" ", i.ToString(), i == _previousTopMatcherIndex ? "*" : " ",
                        matcher.GetDebugInfo());
                    AppendResult result = matcher.Append(input);
                    _traceLogger.Log3("Matcher {MatcherIndex} result: {Result}", i, result);
                    bool isPreviousTopMatcher = i == _previousTopMatcherIndex;
                    _traceLogger.Log2("Is previous top matcher: {IsPreviousTopMatcher}", isPreviousTopMatcher);

                    if (result == AppendResult.NoMatch)
                    {
                        logSb.Insert(0, '❌');
                        if (_previousTopMatcherIndex >= 0 && // there was some top matcher 
                            i >= _previousTopMatcherIndex && // we've reached it or a lower candidate
                            currentTopMatcherIndex == Matchers.Length) // no one yet accepted new character
                        {
                            logSb.Insert(1, "?");
                            AppendResult flushResult = TryFlushStartingFrom(i, resetPendingStartMatcher: true);
                            if (flushResult != AppendResult.Match && canRetry)
                            {
                                // The old prefix is resolved; retry the unconsumed input once.
                                _previousTopMatcherIndex = -1;
                                ProcessSingleInput(input, canRetry: false);
                                return;
                            }
                        }
                        else
                        {
                            matcher.Reset();
                        }
                    }

                    if (result != AppendResult.NoMatch)
                    {
                        logSb.AppendJoin("", " + ", input?.ToString());
                        currentTopMatcherIndex = Math.Min(currentTopMatcherIndex, i);
                        logSb.Insert(0, i == currentTopMatcherIndex ? '*' : '✅');

                        if (i < _previousTopMatcherIndex)
                        {
                            logSb.Insert(1, "?");
                            TryFlushStartingFrom(_previousTopMatcherIndex);
                            _previousTopMatcherIndex = currentTopMatcherIndex;
                        }
                    }

                    if (result == AppendResult.AutoFlushed)
                        /* && isPreviousTopMatcher having no idea what for this was needed. But if something was autoflashed, everything else should be reset because each single input must be processed only once*/
                    {
                        logSb.Insert(0, "⚡");
                        ResetMatchersFrom(0);
                        _previousTopMatcherIndex =
                            currentTopMatcherIndex = -1; // redundant set of _previousTopMatcherIndex for readability
                        break;
                    }
                }
                finally
                {
                    logSb.AppendLine();
                    logAllSb.Append(logSb);
                }
            }

            logAllSb.AppendLine("-----------------------------------------------------");
            logAllSb.AppendLine(
                "Legend: * - top matcher, ✅ - match, ❌ - no match, ⚡ - auto flushed, ? - reset matchers from this index");
            logAllSb.AppendLine("-----------------------------------------------------");

            _infoLogger.Log2(logAllSb.ToString());

            _infoLogger.Log3(
                "Finished processing input: {Input}, current top matcher index: {CurrentTopMatcherIndex}, previous top matcher index: {PreviousTopMatcherIndex}, updating previous to current",
                input, currentTopMatcherIndex, _previousTopMatcherIndex);
            _previousTopMatcherIndex = currentTopMatcherIndex;
        }

        private AppendResult TryFlushStartingFrom(int startIndex, bool resetPendingStartMatcher = false)
        {
            for (int i = startIndex; i < Matchers.Length; i++)
            {
                switch (Matchers[i].TryFlush())
                {
                    case AppendResult.AutoFlushed:
                    {
                        _infoLogger.Log3(
                            "Matcher {Matcher} at index {MatcherIndex} flushed successfully, resetting matchers from index {NextMatcherIndex}",
                            Matchers[i].GetDebugInfo(), i, i + 1);
                        ResetMatchersFrom(i + 1);
                        return AppendResult.AutoFlushed;
                    }

                    case AppendResult.Match:
                    {
                        if (resetPendingStartMatcher && i == startIndex)
                        {
                            Matchers[i].Reset();
                            continue;
                        }

                        return AppendResult.Match;
                    }

                    case AppendResult.NoMatch:
                    {
                        Matchers[i].Reset();
                        continue;
                    }
                }
            }

            return AppendResult.NoMatch;
        }

        private void ResetMatchersFrom(int startIndex)
        {
            for (int i = startIndex; i < Matchers.Length; i++)
                Matchers[i].Reset();
        }

        private void LogMatchersState()
        {
            StringBuilder sb = new("Current matchers state(* - top matcher): ");
            sb.AppendLine();
            sb.AppendLine("-----------------------------------------------------");
            for (int i = 0; i < Matchers.Length; i++)
            {
                IMatcher<T> matcher = Matchers[i];
                sb.AppendJoin(" ", i.ToString(), i == _previousTopMatcherIndex ? "*" : " ", matcher.GetDebugInfo());
                sb.AppendLine();
            }

            sb.AppendLine("-----------------------------------------------------");
            _infoLogger.Log2(sb.ToString());
        }
    }
}
