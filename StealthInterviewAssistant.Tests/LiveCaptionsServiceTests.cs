using System;
using System.Linq;
using Xunit;
using StealthInterviewAssistant.Services;

namespace StealthInterviewAssistant.Tests
{
    public class LiveCaptionsServiceTests
    {
        [Fact]
        public void AppendDelta_WithNewLines_AddsAllLines()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string current = "Line 1\nLine 2\nLine 3";

            // Act
            service.AppendDelta(current);

            // Assert
            string result = service.GetAll();
            Assert.Contains("Line 1", result);
            Assert.Contains("Line 2", result);
            Assert.Contains("Line 3", result);
        }

        [Fact]
        public void AppendDelta_WithDuplicateLines_SkipsDuplicates()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string first = "Line 1\nLine 2";
            string second = "Line 1\nLine 2\nLine 3"; // Line 1 and Line 2 are duplicates

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            string result = service.GetAll();
            // Should only contain Line 3 (the new line)
            Assert.DoesNotContain("Line 1", result);
            Assert.DoesNotContain("Line 2", result);
            Assert.Contains("Line 3", result);
        }

        [Fact]
        public void AppendDelta_WithCaseVariations_NormalizesAndDeduplicates()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string first = "Hello World";
            string second = "HELLO WORLD"; // Same line, different case

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            string result = service.GetAll();
            // Should only contain the first occurrence
            var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Equal("Hello World", lines[0]);
        }

        [Fact]
        public void AppendDelta_WithWhitespaceVariations_NormalizesAndDeduplicates()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string first = "Hello   World";
            string second = "Hello World"; // Same line, different whitespace

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            string result = service.GetAll();
            // Should only contain the first occurrence
            var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Equal("Hello   World", lines[0]);
        }

        [Fact]
        public void AppendDelta_WithMixedCaseAndWhitespace_NormalizesCorrectly()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string first = "  Hello   World  ";
            string second = "hello world"; // Same content, different case and whitespace

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            string result = service.GetAll();
            // Should only contain the first occurrence (preserves original formatting)
            var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Equal("  Hello   World  ", lines[0]);
        }

        [Fact]
        public void AppendDelta_WithWindowsNewlines_HandlesCorrectly()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string text = "Line 1\r\nLine 2\r\nLine 3";

            // Act
            service.AppendDelta(text);

            // Assert
            string result = service.GetAll();
            Assert.Contains("Line 1", result);
            Assert.Contains("Line 2", result);
            Assert.Contains("Line 3", result);
        }

        [Fact]
        public void AppendDelta_WithUnixNewlines_HandlesCorrectly()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string text = "Line 1\nLine 2\nLine 3";

            // Act
            service.AppendDelta(text);

            // Assert
            string result = service.GetAll();
            Assert.Contains("Line 1", result);
            Assert.Contains("Line 2", result);
            Assert.Contains("Line 3", result);
        }

        [Fact]
        public void AppendDelta_WithIncrementalUpdates_OnlyAddsNewLines()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string first = "Line 1\nLine 2";
            string second = "Line 1\nLine 2\nLine 3"; // Incremental update

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            string result = service.GetAll();
            // Should only contain Line 3 (the new line)
            var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Equal("Line 3", lines[0]);
        }

        [Fact]
        public void AppendDelta_WithRingBufferOverflow_RemovesOldestHashes()
        {
            // Arrange
            var service = new LiveCaptionsService();
            const int RING_BUFFER_SIZE = 50;

            // Add 50 unique lines
            for (int i = 0; i < RING_BUFFER_SIZE; i++)
            {
                service.AppendDelta($"Line {i}");
            }

            // Act: Add a duplicate of the first line (should be skipped)
            service.AppendDelta("Line 0");
            
            // Add a new line (should be added)
            service.AppendDelta("Line 50");

            // Assert
            string result = service.GetAll();
            var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            
            // Should have 51 lines (50 original + 1 new, duplicate skipped)
            Assert.Equal(RING_BUFFER_SIZE + 1, lines.Length);
            Assert.Contains("Line 50", result);
        }

        [Fact]
        public void AppendDelta_WithEmptyString_DoesNotAddAnything()
        {
            // Arrange
            var service = new LiveCaptionsService();

            // Act
            service.AppendDelta("");

            // Assert
            string result = service.GetAll();
            Assert.Empty(result);
        }

        [Fact]
        public void AppendDelta_WithNullString_DoesNotThrow()
        {
            // Arrange
            var service = new LiveCaptionsService();

            // Act & Assert
            var exception = Record.Exception(() => service.AppendDelta(null!));
            Assert.Null(exception);
        }

        [Fact]
        public void AppendDelta_WithPrefixMatching_HandlesCorrectly()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string first = "Hello World";
            string second = "Hello World, how are you?"; // Extends the first

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            string result = service.GetAll();
            // Should contain the extension part
            Assert.Contains(", how are you?", result);
        }

        [Fact]
        public void AppendDelta_OnNewTextEvent_FiresWithDeduplicatedContent()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string? eventText = null;
            service.OnNewText += (text) => eventText = text;

            string first = "Line 1\nLine 2";
            string second = "Line 1\nLine 2\nLine 3"; // Line 1 and 2 are duplicates

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            Assert.NotNull(eventText);
            // Event should only contain the new line (Line 3)
            Assert.DoesNotContain("Line 1", eventText);
            Assert.DoesNotContain("Line 2", eventText);
            Assert.Contains("Line 3", eventText);
        }

        [Fact]
        public void Clear_ResetsRingBuffer()
        {
            // Arrange
            var service = new LiveCaptionsService();
            service.AppendDelta("Line 1\nLine 2");

            // Act
            service.Clear();
            service.AppendDelta("Line 1\nLine 2"); // Same lines again

            // Assert
            string result = service.GetAll();
            // After clear, these should be treated as new
            var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
        }

        [Fact]
        public void AppendDelta_WithTabsAndSpaces_NormalizesWhitespace()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string first = "Hello\tWorld";
            string second = "Hello World"; // Same content, tab vs space

            // Act
            service.AppendDelta(first);
            service.AppendDelta(second);

            // Assert
            string result = service.GetAll();
            // Should only contain the first occurrence
            var lines = result.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Equal("Hello\tWorld", lines[0]);
        }

        [Fact]
        public void AppendDelta_WithMixedLineEndings_HandlesAll()
        {
            // Arrange
            var service = new LiveCaptionsService();
            string text = "Line 1\r\nLine 2\nLine 3\rLine 4";

            // Act
            service.AppendDelta(text);

            // Assert
            string result = service.GetAll();
            Assert.Contains("Line 1", result);
            Assert.Contains("Line 2", result);
            Assert.Contains("Line 3", result);
            Assert.Contains("Line 4", result);
        }
    }
}

