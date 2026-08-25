using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Pins the rendered text of the header-less facade through the internal formatting seam, the
    /// FNV-1a hash and palette contract, and the severity to console-channel mapping.
    /// </summary>
    [TestFixture]
    public class LogTests
    {
        private static readonly string[] _paletteHex =
        {
            "D25F5F", "D08540", "B8912A", "8AA13C", "4FA061", "3FA39B",
            "4193C4", "5A7FD0", "7B6FD6", "A464C8", "C75FA8", "C9607D"
        };

        private sealed class NullToString
        {
            public override string ToString()
            {
                return null;
            }
        }

        private sealed class NamedProbe
        {
            public override string ToString()
            {
                return "probe-to-string";
            }
        }

        private static Regex Exact(string message)
        {
            // Rich text is regex metacharacter soup, and Unity may append a stack trace to the text
            // LogAssert matches against, so escape the expectation and leave it unanchored.
            return new Regex(Regex.Escape(message));
        }

        [Test]
        public void LogSeverity_HasStableOrdinals()
        {
            Assert.AreEqual(0, (int)LogSeverity.Info);
            Assert.AreEqual(1, (int)LogSeverity.Warning);
            Assert.AreEqual(2, (int)LogSeverity.Error);
            Assert.AreEqual(3, (int)LogSeverity.Critical);
        }

        [Test]
        public void FormatMessage_NullPrefix_ReturnsBodyUnchanged()
        {
            Assert.AreEqual("hello", CustomLogger.FormatMessage(null, LogSeverity.Info, "hello"));
            Assert.AreEqual("hello", CustomLogger.FormatMessage(null, LogSeverity.Warning, "hello"));
            Assert.AreEqual("hello", CustomLogger.FormatMessage(null, LogSeverity.Error, "hello"));
        }

        [Test]
        public void FormatMessage_Critical_WrapsBodyInRed()
        {
            Assert.AreEqual(
                "<color=#E04B4B>meltdown</color>",
                CustomLogger.FormatMessage(null, LogSeverity.Critical, "meltdown"));
        }

        [Test]
        public void FormatMessage_Critical_WrapsOnlyTheBody_PrefixStaysOutside()
        {
            Assert.AreEqual(
                "<color=#5A7FD0>[Analytics]</color> <color=#E04B4B>meltdown</color>",
                CustomLogger.FormatMessage(
                    "<color=#5A7FD0>[Analytics]</color> ", LogSeverity.Critical, "meltdown"));
        }

        [Test]
        public void FormatMessage_NullMessage_RendersNullLiteral()
        {
            Assert.AreEqual("Null", CustomLogger.FormatMessage(null, LogSeverity.Info, null));
            Assert.AreEqual("p:Null", CustomLogger.FormatMessage("p:", LogSeverity.Info, null));
        }

        [Test]
        public void FormatMessage_MessageWhoseToStringReturnsNull_RendersNullLiteral()
        {
            Assert.AreEqual(
                "Null", CustomLogger.FormatMessage(null, LogSeverity.Info, new NullToString()));
        }

        [Test]
        public void FormatMessage_NonStringMessage_UsesToString()
        {
            Assert.AreEqual("42", CustomLogger.FormatMessage(null, LogSeverity.Info, 42));
            Assert.AreEqual(
                "probe-to-string",
                CustomLogger.FormatMessage(null, LogSeverity.Info, new NamedProbe()));
        }

        [Test]
        public void Hash_EmptyString_ReturnsOffsetBasis()
        {
            Assert.AreEqual(2166136261u, CustomLogger.Hash(string.Empty));
        }

        [Test]
        public void Hash_KnownHeaders_MatchFnv1a32()
        {
            Assert.AreEqual(3165511723u, CustomLogger.Hash("Analytics"));
            Assert.AreEqual(1294818664u, CustomLogger.Hash("Save"));
            Assert.AreEqual(231048258u, CustomLogger.Hash("Physics"));
        }

        [Test]
        public void Hash_IsCaseSensitive()
        {
            Assert.AreNotEqual(CustomLogger.Hash("Save"), CustomLogger.Hash("save"));
        }

        [Test]
        public void Palette_MatchesPinnedHexInOrder()
        {
            // Palette order is part of the contract: reordering it silently recolours every channel
            // in every consuming project.
            Assert.AreEqual(_paletteHex.Length, CustomLogger.Palette.Length);
            for (var i = 0; i < _paletteHex.Length; i++)
            {
                Assert.AreEqual(_paletteHex[i], CustomLogger.ToHex(CustomLogger.Palette[i]));
            }
        }

        [Test]
        public void Palette_EntriesAreDistinct()
        {
            for (var i = 0; i < _paletteHex.Length; i++)
            {
                for (var j = i + 1; j < _paletteHex.Length; j++)
                {
                    Assert.AreNotEqual(_paletteHex[i], _paletteHex[j]);
                }
            }
        }

        [Test]
        public void Palette_EntriesAreMidBrightness_SoTheyReadOnBothEditorSkins()
        {
            foreach (var entry in CustomLogger.Palette)
            {
                var luminance = (0.299f * entry.r + 0.587f * entry.g + 0.114f * entry.b) / 255f;
                Assert.That(luminance, Is.InRange(0.35f, 0.72f));
                Assert.AreEqual(255, entry.a);
            }
        }

        [Test]
        public void PickColor_IsDeterministicForTheSameHeader()
        {
            var first = CustomLogger.PickColor("Analytics");
            var second = CustomLogger.PickColor("Analytics");

            Assert.AreEqual(CustomLogger.ToHex(first), CustomLogger.ToHex(second));
        }

        [Test]
        public void PickColor_KnownHeaders_LandOnTheirPaletteSlot()
        {
            Assert.AreEqual("5A7FD0", CustomLogger.ToHex(CustomLogger.PickColor("Analytics")));
            Assert.AreEqual("4FA061", CustomLogger.ToHex(CustomLogger.PickColor("Save")));
            Assert.AreEqual("4193C4", CustomLogger.ToHex(CustomLogger.PickColor("Physics")));
        }

        [Test]
        public void PickColor_DifferentHeaders_CanLandOnDifferentSlots()
        {
            Assert.AreNotEqual(
                CustomLogger.ToHex(CustomLogger.PickColor("Analytics")),
                CustomLogger.ToHex(CustomLogger.PickColor("Save")));
        }

        [Test]
        public void ToHex_RoundsRatherThanTruncates()
        {
            Assert.AreEqual("FF0000", CustomLogger.ToHex(new Color(1f, 0f, 0f)));
            Assert.AreEqual("000000", CustomLogger.ToHex(new Color(0f, 0f, 0f)));
            Assert.AreEqual("808080", CustomLogger.ToHex(new Color(0.5f, 0.5f, 0.5f)));
            Assert.AreEqual("D25F5F", CustomLogger.ToHex(CustomLogger.Palette[0]));
        }

        [Test]
        public void ToHex_ClampsOutOfRangeChannels()
        {
            Assert.AreEqual("FF0000", CustomLogger.ToHex(new Color(4f, -2f, 0f)));
        }

        [Test]
        public void BuildPrefix_ProducesRichTextHeaderWithTrailingSpace()
        {
            Assert.AreEqual(
                "<color=#4FA061>[Save]</color> ",
                CustomLogger.BuildPrefix("Save", CustomLogger.Palette[4]));
        }

        [Test]
        public void Info_GoesToTheLogChannel()
        {
            LogAssert.Expect(LogType.Log, Exact("log-facade-info"));

            Log.Info("log-facade-info");
        }

        [Test]
        public void Warning_GoesToTheWarningChannel()
        {
            LogAssert.Expect(LogType.Warning, Exact("log-facade-warning"));

            Log.Warning("log-facade-warning");
        }

        [Test]
        public void Error_GoesToTheErrorChannel()
        {
            LogAssert.Expect(LogType.Error, Exact("log-facade-error"));

            Log.Error("log-facade-error");
        }

        [Test]
        public void Critical_GoesToTheErrorChannel_WithRedBody()
        {
            LogAssert.Expect(LogType.Error, Exact("<color=#E04B4B>log-facade-critical</color>"));

            Log.Critical("log-facade-critical");
        }

        [Test]
        public void Message_RoutesEachSeverityLikeTheDedicatedMethods()
        {
            LogAssert.Expect(LogType.Log, Exact("message-info"));
            LogAssert.Expect(LogType.Warning, Exact("message-warning"));
            LogAssert.Expect(LogType.Error, Exact("message-error"));
            LogAssert.Expect(LogType.Error, Exact("<color=#E04B4B>message-critical</color>"));

            Log.Message(LogSeverity.Info, "message-info");
            Log.Message(LogSeverity.Warning, "message-warning");
            Log.Message(LogSeverity.Error, "message-error");
            Log.Message(LogSeverity.Critical, "message-critical");
        }

        [Test]
        public void Message_UnknownSeverity_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Log.Message((LogSeverity)99, "unknown-severity"));
        }

        [Test]
        public void Info_NullMessage_LogsTheNullLiteral()
        {
            LogAssert.Expect(LogType.Log, Exact("Null"));

            Log.Info(null);
        }
    }
}
