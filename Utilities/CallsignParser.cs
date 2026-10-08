using System;
using System.Text.RegularExpressions;

namespace opentuner.Utilities
{
    /// <summary>
    /// Extracts an amateur radio callsign from a free-text service name.
    /// Service names often look like "G4KLB", "M0DNY John", "F5OEO/P test"
    /// or "GB3IT 1080p" - we want the callsign token in each case.
    /// </summary>
    public static class CallsignParser
    {
        // Standard callsign shapes: letters-then-digit-then-letters (G4KLB, VK3ABC)
        // or digit-letter-digit(s)-letters (9A1A). A trailing /P /M /MM is allowed.
        // Pure video-resolution tokens (1080p) and numeric ranges (1500/1000) are rejected.
        private static readonly Regex CallsignRegex = new Regex(
            @"\b(?:[A-Z]{1,2}[0-9][A-Z]{1,4}|[0-9][A-Z][0-9]{1,2}[A-Z]{1,4})(?:/[A-Z0-9]{1,6})*\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Returns the first callsign-looking token in the text, or null.
        /// The portable suffix (e.g. "/P") is stripped for lookups.
        /// </summary>
        public static string Extract(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var match = CallsignRegex.Match(text.ToUpperInvariant());
            if (!match.Success)
                return null;

            return StripSuffix(match.Value);
        }

        /// <summary>Strips a /P, /M, /MM etc suffix from a callsign.</summary>
        public static string StripSuffix(string callsign)
        {
            if (string.IsNullOrEmpty(callsign))
                return callsign;

            int slash = callsign.IndexOf('/');
            return slash > 0 ? callsign.Substring(0, slash) : callsign;
        }
    }
}
