using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Mote.Testing
{
    /// <summary>Drains owned process pipes while retaining only bounded fixed menu diagnostics.</summary>
    public static class MacGridMenuTraceCapture
    {
        /// <summary>The protocol contains no free-form text, paths, identifiers, or document content.</summary>
        private static readonly Regex Pattern = new Regex(
            @"\Amote-grid-menu-v1 phase=(show-enter|native-return|schedule-return|popup-begin|popup-return|will-open|did-close) seq=([1-9]|1[0-6]) requests=([0-9]|1[0-6]) opens=([0-9]|1[0-6]) closes=([0-9]|1[0-6]) open=[01] result=(-1|[01]) configured=[01] items=(-1|[0-9]|1[0-6]) coordinate=[01] shown=[01] key=[01] first=[01] active=[01]\z",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Reads asynchronously until EOF, discarding arbitrary and overlong lines without retaining them.
        /// At most sixteen valid lines of at most 384 characters are returned. With retain false,
        /// the stream is drained but nothing is retained; this does not certify absence of errors.
        /// </summary>
        public static async Task<string[]> ReadAsync(TextReader reader, bool retain)
        {
            var accepted = new List<string>(16);
            var buffer = new char[1024];
            var line = new StringBuilder(384);
            bool overlong = false;
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
            {
                for (int i = 0; i < count; i++)
                {
                    char value = buffer[i];
                    if (value == '\n')
                    {
                        Accept(line, overlong, retain, accepted);
                        line.Clear();
                        overlong = false;
                    }
                    else if (!overlong)
                    {
                        if (line.Length == 384) { overlong = true; line.Clear(); }
                        else { line.Append(value); }
                    }
                }
            }
            Accept(line, overlong, retain, accepted);
            return accepted.ToArray();
        }

        /// <summary>Rejects malformed protocol rows, including inconsistent phase/result combinations.</summary>
        private static void Accept(StringBuilder line, bool overlong, bool retain, List<string> accepted)
        {
            if (!retain || overlong || accepted.Count == 16) { return; }
            string value = line.ToString();
            if (value.EndsWith("\r", StringComparison.Ordinal)) { value = value.Substring(0, value.Length - 1); }
            Match match = Pattern.Match(value);
            if (!match.Success) { return; }
            string phase = match.Groups[1].Value;
            bool returning = phase == "native-return" || phase == "schedule-return" || phase == "popup-return";
            bool unknownResult = match.Groups[6].Value == "-1";
            if (returning == unknownResult) { return; }
            accepted.Add(value);
        }
    }
}
