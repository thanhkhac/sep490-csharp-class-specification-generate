using System.IO;
using System.Text.RegularExpressions;

namespace Sep490ClassDocumentGenerator
{
    public class IgnoreManager
    {
        private readonly string _ignoreFilePath;
        public List<string> Rules { get; private set; }

        public IgnoreManager(string folderPath)
        {
            _ignoreFilePath = Path.Combine(folderPath, "generate.ignore");
            LoadRules();
        }

        public void LoadRules()
        {
            Rules = File.Exists(_ignoreFilePath)
                ? File.ReadAllLines(_ignoreFilePath)
                    .Select(x => x.Trim())
                    .Where(x => !string.IsNullOrEmpty(x) && !x.StartsWith("#"))
                    .ToList()
                : new List<string>();
        }

        public void SaveRules()
        {
            File.WriteAllLines(_ignoreFilePath, Rules);
        }

        public bool IsIgnored(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return false;
            
            // Normalize path separators
            relativePath = relativePath.Replace('\\', '/');
            
            foreach (var pattern in Rules)
            {
                if (IsMatch(pattern, relativePath))
                {
                    return true;
                }
            }
            
            return false;
        }

        private bool IsMatch(string pattern, string path)
        {
            // Normalize pattern
            pattern = pattern.Replace('\\', '/');
            
            // Handle different pattern types
            if (pattern.EndsWith("/**"))
            {
                // Folder pattern: matches folder and all its contents
                var folderPattern = pattern.Substring(0, pattern.Length - 3);
                // Check if path is exactly the folder OR starts with folder/
                return path == folderPattern || path.StartsWith(folderPattern + "/");
            }
            else if (pattern.EndsWith("/*"))
            {
                // Direct children pattern
                var folderPattern = pattern.Substring(0, pattern.Length - 2);
                return path.StartsWith(folderPattern + "/") && 
                       !path.Substring(folderPattern.Length + 1).Contains('/');
            }
            else if (pattern.Contains("*") || pattern.Contains("?"))
            {
                // Wildcard pattern - convert to regex
                try
                {
                    var regexPattern = "^" + Regex.Escape(pattern)
                        .Replace(@"\*", ".*")
                        .Replace(@"\?", ".") + "$";
                    return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                // Exact match
                return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }

        public void AddRule(string pattern)
        {
            if (!Rules.Contains(pattern)) 
            {
                Rules.Add(pattern);
            }
        }

        public void RemoveRule(string pattern)
        {
            Rules.Remove(pattern);
        }

        public void ReplaceRule(string oldPattern, string newPattern)
        {
            var index = Rules.IndexOf(oldPattern);
            if (index >= 0) Rules[index] = newPattern;
        }
    }
}