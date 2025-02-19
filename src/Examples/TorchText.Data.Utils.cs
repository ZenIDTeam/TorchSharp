using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TorchText.Data
{
    public static partial class Utils
    {
        public static Func<string, IEnumerable<string>> get_tokenizer(string name)
        {
            if (name == "basic_english") return BasicEnglish;
            throw new NotImplementedException($"The '{name}' text tokenizer is not implemented.");
        }


        private static string[] _patterns = new string []{
             "\'",
             "\"",
             "\\.",
             "<br \\/>",
             ",",
             "\\(",
             "\\)",
             "\\!",
             "\\?",
             "\\;",
             "\\:",
             "\\\\",
             "\\s+",
        };
        private static string[] _replacements = new string[] {
            " \\'  ",
            "",
            " . ",
            " ",
            " , ",
            " ( ",
            " ) ",
            " ! ",
            " ? ",
            " ",
            " ",
            " ",
            " "
        };

        private static IEnumerable<string> BasicEnglish(string input)
        {
            if (_patterns.Length != _replacements.Length) 
                throw new InvalidProgramException("internal error: patterns and replacements are not the same length");

            input = input.Trim().ToLowerInvariant();

            for (var i = 0; i < _patterns.Length; ++i) {
                input = Regex.Replace(input, _patterns[i], _replacements[i]);
            }
            return input.Split(' ');
        }
    }
}
