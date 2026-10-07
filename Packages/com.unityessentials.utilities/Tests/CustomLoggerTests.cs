using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Pins the header prefix a logger precomputes, the determinism of its auto-assigned colour,
    /// header validation, the header-less constructor, and the severity to console-channel mapping
    /// of its log methods.
    /// </summary>
    [TestFixture]
    public class CustomLoggerTests
    {
        private const string AnalyticsPrefix = "<color=#5A7FD0>[Analytics]</color> ";

        private static Regex Exact(string message)
        {
            // Rich text is regex metacharacter soup, and Unity may append a stack trace to the text
            // LogAssert matches against, so escape the expectation and leave it unanchored.
            return new Regex(Regex.Escape(message));
        }

        [Test]
        public void Constructor_NullHeader_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new CustomLogger(null));
            Assert.Throws<ArgumentException>(() => new CustomLogger(null, Color.red));
        }

        [Test]
        public void Constructor_EmptyHeader_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new CustomLogger(string.Empty));
            Assert.Throws<ArgumentException>(() => new CustomLogger(string.Empty, Color.red));
        }

        [Test]
        public void Constructor_WhitespaceHeader_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new CustomLogger("   "));
            Assert.Throws<ArgumentException>(() => new CustomLogger("\t", Color.red));
        }

        [Test]
        public void Constructor_ThrowsWithTheHeaderParameterName()
        {
            var exception = Assert.Throws<ArgumentException>(() => new CustomLogger(" "));

            Assert.AreEqual("header", exception.ParamName);
        }

        [Test]
        public void Constructor_Parameterless_HasNoHeaderAndNoPrefix()
        {
            var logger = new CustomLogger();

            Assert.IsNull(logger.Header);
            Assert.IsNull(logger.Prefix);
            Assert.AreEqual(default(Color), logger.Color);
        }

        [Test]
        public void Constructor_KeepsHeaderVerbatim()
        {
            Assert.AreEqual("Save System", new CustomLogger("Save System").Header);
        }

        [Test]
        public void Constructor_AutoColor_TakesThePaletteSlotForItsHeader()
        {
            var logger = new CustomLogger("Analytics");

            Assert.AreEqual("5A7FD0", CustomLogger.ToHex(logger.Color));
            Assert.AreEqual(AnalyticsPrefix, logger.Prefix);
        }

        [Test]
        public void Constructor_AutoColor_IsDeterministicAcrossInstances()
        {
            Assert.AreEqual(new CustomLogger("Analytics").Prefix, new CustomLogger("Analytics").Prefix);
        }

        [Test]
        public void Constructor_AutoColor_DifferentHeadersCanTakeDifferentSlots()
        {
            Assert.AreNotEqual(
                CustomLogger.ToHex(new CustomLogger("Analytics").Color),
                CustomLogger.ToHex(new CustomLogger("Save").Color));
        }

        [Test]
        public void Constructor_ExplicitColor_IsUsedVerbatimInThePrefix()
        {
            var logger = new CustomLogger("UI", new Color(1f, 0f, 0f));

            Assert.AreEqual(new Color(1f, 0f, 0f), logger.Color);
            Assert.AreEqual("<color=#FF0000>[UI]</color> ", logger.Prefix);
        }

        [Test]
        public void Constructor_ExplicitColor_OverridesTheHashedPaletteSlot()
        {
            var logger = new CustomLogger("Analytics", new Color(0f, 1f, 0f));

            Assert.AreEqual("<color=#00FF00>[Analytics]</color> ", logger.Prefix);
        }

        [Test]
        public void FormatMessage_WithLoggerPrefix_PutsTheHeaderInFront()
        {
            var logger = new CustomLogger("Analytics");

            Assert.AreEqual(
                AnalyticsPrefix + "session started",
                CustomLogger.FormatMessage(logger.Prefix, LogSeverity.Info, "session started"));
        }

        [Test]
        public void FormatMessage_Critical_KeepsThePrefixOutsideTheRedBody()
        {
            var logger = new CustomLogger("Analytics");

            Assert.AreEqual(
                AnalyticsPrefix + "<color=#E04B4B>upload failed</color>",
                CustomLogger.FormatMessage(logger.Prefix, LogSeverity.Critical, "upload failed"));
        }

        [Test]
        public void FormatMessage_NullMessage_KeepsThePrefixAndRendersNullLiteral()
        {
            var logger = new CustomLogger("Analytics");

            Assert.AreEqual(
                AnalyticsPrefix + "Null",
                CustomLogger.FormatMessage(logger.Prefix, LogSeverity.Info, null));
        }

        [Test]
        public void Info_GoesToTheLogChannel()
        {
            var logger = new CustomLogger("Analytics");
            LogAssert.Expect(LogType.Log, Exact(AnalyticsPrefix + "logger-info"));

            logger.Info("logger-info");
        }

        [Test]
        public void Warning_GoesToTheWarningChannel()
        {
            var logger = new CustomLogger("Analytics");
            LogAssert.Expect(LogType.Warning, Exact(AnalyticsPrefix + "logger-warning"));

            logger.Warning("logger-warning");
        }

        [Test]
        public void Error_GoesToTheErrorChannel()
        {
            var logger = new CustomLogger("Analytics");
            LogAssert.Expect(LogType.Error, Exact(AnalyticsPrefix + "logger-error"));

            logger.Error("logger-error");
        }

        [Test]
        public void Critical_GoesToTheErrorChannel_WithRedBody()
        {
            var logger = new CustomLogger("Analytics");
            LogAssert.Expect(
                LogType.Error, Exact(AnalyticsPrefix + "<color=#E04B4B>logger-critical</color>"));

            logger.Critical("logger-critical");
        }

        [Test]
        public void Message_RoutesEachSeverityLikeTheDedicatedMethods()
        {
            var logger = new CustomLogger("Analytics");
            LogAssert.Expect(LogType.Log, Exact(AnalyticsPrefix + "logger-message-info"));
            LogAssert.Expect(LogType.Warning, Exact(AnalyticsPrefix + "logger-message-warning"));
            LogAssert.Expect(LogType.Error, Exact(AnalyticsPrefix + "logger-message-error"));
            LogAssert.Expect(
                LogType.Error,
                Exact(AnalyticsPrefix + "<color=#E04B4B>logger-message-critical</color>"));

            logger.Message(LogSeverity.Info, "logger-message-info");
            logger.Message(LogSeverity.Warning, "logger-message-warning");
            logger.Message(LogSeverity.Error, "logger-message-error");
            logger.Message(LogSeverity.Critical, "logger-message-critical");
        }

        [Test]
        public void Message_UnknownSeverity_ThrowsArgumentOutOfRangeException()
        {
            var logger = new CustomLogger("Analytics");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => logger.Message((LogSeverity)99, "unknown-severity"));
        }

        [Test]
        public void Info_NullMessage_LogsThePrefixedNullLiteral()
        {
            var logger = new CustomLogger("Analytics");
            LogAssert.Expect(LogType.Log, Exact(AnalyticsPrefix + "Null"));

            logger.Info(null);
        }

        [Test]
        public void Info_HeaderlessLogger_LogsTheBareMessage()
        {
            var logger = new CustomLogger();
            LogAssert.Expect(LogType.Log, Exact("headerless-info"));

            logger.Info("headerless-info");
        }

        [Test]
        public void Critical_HeaderlessLogger_WrapsOnlyTheBodyInRed()
        {
            var logger = new CustomLogger();
            LogAssert.Expect(LogType.Error, Exact("<color=#E04B4B>headerless-critical</color>"));

            logger.Critical("headerless-critical");
        }
    }
}
