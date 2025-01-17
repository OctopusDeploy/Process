// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Octopus Modification: This code is derived from
// https://github.com/dotnet/runtime/blob/9f54e5162a177b8d6ad97ba53c6974fb02d0a47d/src/libraries/System.Private.CoreLib/src/System/PasteArguments.cs
// .NET uses it to implement the ProcessStartInfo.ArgumentList property, which we do not have access to in .NET Framework.
// We copy it here to provide a polyfill for older frameworks
//
// The following changes have been made:
// - Namespace changed to Octopus.Shellfish
// - Made class non-partial
// - Changed to use regular StringBuilder as ValueStringBuilder is internal to the CLR
// - Added static JoinArguments function to encapsulate the logic of joining arguments

using System;
using System.Collections.Generic;
using System.Text;

namespace Octopus.Shellfish;

static class PasteArguments
{
    const char Quote = '\"';
    const char Backslash = '\\';

    internal static string JoinArguments(IEnumerable<string> arguments)
    {
        var stringBuilder = new StringBuilder();
        foreach (var argument in arguments)
            AppendArgument(stringBuilder, argument);

        return stringBuilder.ToString();
    }

    internal static void AppendArgument(StringBuilder stringBuilder, string argument)
    {
        if (stringBuilder.Length != 0)
            stringBuilder.Append(' ');

        // Parsing rules for non-argv[0] arguments:
        //   - Backslash is a normal character except followed by a quote.
        //   - 2N backslashes followed by a quote ==> N literal backslashes followed by unescaped quote
        //   - 2N+1 backslashes followed by a quote ==> N literal backslashes followed by a literal quote
        //   - Parsing stops at first whitespace outside of quoted region.
        //   - (post 2008 rule): A closing quote followed by another quote ==> literal quote, and parsing remains in quoting mode.
        if (argument.Length != 0 && ContainsNoWhitespaceOrQuotes(argument))
        {
            // Simple case - no quoting or changes needed.
            stringBuilder.Append(argument);
        }
        else
        {
            stringBuilder.Append(Quote);
            var idx = 0;
            while (idx < argument.Length)
            {
                var c = argument[idx++];
                if (c == Backslash)
                {
                    var numBackSlash = 1;
                    while (idx < argument.Length && argument[idx] == Backslash)
                    {
                        idx++;
                        numBackSlash++;
                    }

                    if (idx == argument.Length)
                    {
                        // We'll emit an end quote after this so must double the number of backslashes.
                        stringBuilder.Append(Backslash, numBackSlash * 2);
                    }
                    else if (argument[idx] == Quote)
                    {
                        // Backslashes will be followed by a quote. Must double the number of backslashes.
                        stringBuilder.Append(Backslash, numBackSlash * 2 + 1);
                        stringBuilder.Append(Quote);
                        idx++;
                    }
                    else
                    {
                        // Backslash will not be followed by a quote, so emit as normal characters.
                        stringBuilder.Append(Backslash, numBackSlash);
                    }

                    continue;
                }

                if (c == Quote)
                {
                    // Escape the quote so it appears as a literal. This also guarantees that we won't end up generating a closing quote followed
                    // by another quote (which parses differently pre-2008 vs. post-2008.)
                    stringBuilder.Append(Backslash);
                    stringBuilder.Append(Quote);
                    continue;
                }

                stringBuilder.Append(c);
            }

            stringBuilder.Append(Quote);
        }
    }

    static bool ContainsNoWhitespaceOrQuotes(string s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c) || c == Quote)
                return false;
        }

        return true;
    }
}