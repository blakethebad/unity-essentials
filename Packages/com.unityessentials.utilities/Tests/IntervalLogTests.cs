using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Pins call-site interval logging: which calls reach the console and which are suppressed, that
    /// each source line counts on its own, that counters survive nothing but a reset, and that the
    /// facade attributes a call to its caller rather than to itself.
    /// </summary>
    [TestFixture]
    public class IntervalLogTests
    {
        private CustomLogger _logger;

        private static Regex Exact(string message)
        {
            // Rich text is regex metacharacter soup, and Unity may append a stack trace to the text
            // LogAssert matches against, so escape the expectation and leave it unanchored.
            return new Regex(Regex.Escape(message));
        }

        [SetUp]
        public void SetUp()
        {
            CustomLogger.ResetIntervalCounts();
            _logger = new CustomLogger();
        }

        [Test]
        public void Interval_FirstCallAtACallSite_LogsImmediately()
        {
            LogAssert.Expect(LogType.Log, Exact("interval-first-call"));

            _logger.Interval("interval-first-call", 1000);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_OfThree_LogsTheFirstCallThenEveryThirdCall()
        {
            LogAssert.Expect(LogType.Log, Exact("interval-three-1"));
            LogAssert.Expect(LogType.Log, Exact("interval-three-4"));
            LogAssert.Expect(LogType.Log, Exact("interval-three-7"));

            for (var call = 1; call <= 7; call++)
            {
                _logger.Interval("interval-three-" + call, 3);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_OfOne_LogsEveryCall()
        {
            for (var call = 1; call <= 4; call++)
            {
                LogAssert.Expect(LogType.Log, Exact("interval-one-" + call));
            }

            for (var call = 1; call <= 4; call++)
            {
                _logger.Interval("interval-one-" + call, 1);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_DifferentSourceLines_KeepIndependentCounters()
        {
            LogAssert.Expect(LogType.Log, Exact("interval-line-a-1"));
            LogAssert.Expect(LogType.Log, Exact("interval-line-b-1"));
            LogAssert.Expect(LogType.Log, Exact("interval-line-a-3"));
            LogAssert.Expect(LogType.Log, Exact("interval-line-b-3"));

            for (var call = 1; call <= 4; call++)
            {
                _logger.Interval("interval-line-a-" + call, 2);
                _logger.Interval("interval-line-b-" + call, 2);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_SameCallSiteFromDifferentLoggers_SharesOneCounter()
        {
            var loggers = new[] { new CustomLogger(), new CustomLogger() };
            LogAssert.Expect(LogType.Log, Exact("interval-shared-0"));

            for (var i = 0; i < loggers.Length; i++)
            {
                loggers[i].Interval("interval-shared-" + i, 5);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_SeverityOverload_GoesToTheWarningChannel()
        {
            LogAssert.Expect(LogType.Warning, Exact("interval-warning"));

            _logger.Interval(LogSeverity.Warning, "interval-warning", 4);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_SeverityOverload_Critical_GoesToTheErrorChannelWithRedBody()
        {
            LogAssert.Expect(LogType.Error, Exact("<color=#E04B4B>interval-critical</color>"));

            _logger.Interval(LogSeverity.Critical, "interval-critical", 4);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_SeverityOverload_SuppressesBetweenIntervalsToo()
        {
            LogAssert.Expect(LogType.Warning, Exact("interval-severity-1"));
            LogAssert.Expect(LogType.Warning, Exact("interval-severity-4"));

            for (var call = 1; call <= 5; call++)
            {
                _logger.Interval(LogSeverity.Warning, "interval-severity-" + call, 3);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_ZeroInterval_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _logger.Interval("interval-zero", 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _logger.Interval(LogSeverity.Warning, "interval-severity-zero", 0));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_NegativeInterval_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _logger.Interval("interval-negative", -3));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _logger.Interval(LogSeverity.Error, "interval-severity-negative", -1));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Interval_ThrowsWithTheIntervalParameterName()
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => _logger.Interval("interval-param-name", 0));

            Assert.AreEqual("interval", exception.ParamName);
        }

        [Test]
        public void ResetIntervalCounts_LetsASuppressedCallSiteLogAgain()
        {
            LogAssert.Expect(LogType.Log, Exact("interval-reset-1"));
            LogAssert.Expect(LogType.Log, Exact("interval-reset-3"));

            for (var call = 1; call <= 4; call++)
            {
                if (call == 3)
                {
                    CustomLogger.ResetIntervalCounts();
                }

                _logger.Interval("interval-reset-" + call, 10);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogInterval_FirstCallAtACallSite_LogsImmediately()
        {
            LogAssert.Expect(LogType.Log, Exact("facade-first-call"));

            Log.Interval("facade-first-call", 1000);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogInterval_DifferentSourceLines_KeepIndependentCounters()
        {
            // Both calls logging is what proves the facade forwards the caller's file and line: were
            // it to capture its own, the two would share Log.cs's counter and the second would be
            // suppressed.
            LogAssert.Expect(LogType.Log, Exact("facade-site-a"));
            LogAssert.Expect(LogType.Log, Exact("facade-site-b"));

            Log.Interval("facade-site-a", 50);
            Log.Interval("facade-site-b", 50);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogInterval_OfFiveOverSixCalls_LogsExactlyTwice()
        {
            LogAssert.Expect(LogType.Log, Exact("facade-loop-1"));
            LogAssert.Expect(LogType.Log, Exact("facade-loop-6"));

            for (var call = 1; call <= 6; call++)
            {
                Log.Interval("facade-loop-" + call, 5);
            }

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogInterval_SeverityOverload_GoesToTheWarningChannel()
        {
            LogAssert.Expect(LogType.Warning, Exact("facade-interval-warning"));

            Log.Interval(LogSeverity.Warning, "facade-interval-warning", 3);

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogInterval_SeverityOverload_DifferentSourceLines_KeepIndependentCounters()
        {
            LogAssert.Expect(LogType.Warning, Exact("facade-severity-site-a"));
            LogAssert.Expect(LogType.Warning, Exact("facade-severity-site-b"));

            Log.Interval(LogSeverity.Warning, "facade-severity-site-a", 50);
            Log.Interval(LogSeverity.Warning, "facade-severity-site-b", 50);

            LogAssert.NoUnexpectedReceived();
        }
    }
}
