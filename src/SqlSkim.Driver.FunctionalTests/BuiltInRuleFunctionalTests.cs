﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis.Sarif.Readers;
using Microsoft.CodeAnalysis.Sarif.Sdk;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.Sql
{
    public class BuiltInRuleFunctionalTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public BuiltInRuleFunctionalTests(ITestOutputHelper output)
        {
            _testOutputHelper = output;
        }

        private static string TestDirectory = GetTestDirectory(@"SqlSkim.Driver.FunctionalTests\RulesTestData");

        private static string GetTestDirectory(string relativeDirectory)
        {
            var codeBaseUrl = new Uri(Assembly.GetExecutingAssembly().CodeBase);
            var codeBasePath = Uri.UnescapeDataString(codeBaseUrl.AbsolutePath);
            var dirPath = Path.GetDirectoryName(codeBasePath);
            dirPath = Path.Combine(dirPath, @"..\..\..\..\src\");
            dirPath = Path.GetFullPath(dirPath);
            return Path.Combine(dirPath, relativeDirectory);
        }

        [Fact]
        public void Driver_BuiltInRuleFunctionalTests()
        {
            BatchRuleRules(string.Empty, "*.sql");
        }

        private void BatchRuleRules(string ruleName, string inputFilter)
        {
            var sb = new StringBuilder();

            string testDirectory = BuiltInRuleFunctionalTests.TestDirectory + "\\" + ruleName;
            string[] testFiles = Directory.GetFiles(testDirectory, inputFilter);

            foreach (string file in testFiles)
            {
                RunRules(sb, file);
            }

            if (sb.Length == 0)
            {
                // Test passes
                return;
            }

            string rebaselineMessage = "If the actual output is expected, generate new baselines by executing `UpdateBaselines.ps1` from a PS command prompt.";
            sb.AppendLine(String.Format(CultureInfo.CurrentCulture, rebaselineMessage));

            if (sb.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Run the following to all test baselines vs. actual results:");
                sb.AppendLine(GenerateDiffCommand(
                    Path.Combine(testDirectory, "Expected"), 
                    Path.Combine(testDirectory, "Actual")));
                _testOutputHelper.WriteLine(sb.ToString());
            }

            Assert.Equal(0, sb.Length);
        }

        private void RunRules(StringBuilder sb, string inputFileName)
        {
            string fileName = Path.GetFileName(inputFileName);
            string actualDirectory = Path.Combine(Path.GetDirectoryName(inputFileName), "Actual");
            string expectedDirectory = Path.Combine(Path.GetDirectoryName(inputFileName), "Expected");

            if (Directory.Exists(actualDirectory))
            {
                Directory.Delete(actualDirectory, true);
            }
            Directory.CreateDirectory(actualDirectory);

            string expectedFileName = Path.Combine(expectedDirectory, fileName + ".sarif");
            string actualFileName = Path.Combine(actualDirectory, fileName + ".sarif");

            AnalyzeCommand command = new AnalyzeCommand();
            AnalyzeOptions options = new AnalyzeOptions();

            options.TargetFileSpecifiers = new string[] { inputFileName };
            options.OutputFilePath = actualFileName;
            options.Verbose = true;
            options.Recurse = false;
            options.ConfigurationFilePath = "default";

            int result = command.Run(options);

            Assert.Equal(0, result);

            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                ContractResolver = SarifContractResolver.Instance,
                Formatting = Formatting.Indented
            };

            string expectedText = File.ReadAllText(expectedFileName);
            string actualText = File.ReadAllText(actualFileName);

            // Make sure we can successfully deserialize what was just generated
            ResultLog expectedLog = JsonConvert.DeserializeObject<ResultLog>(expectedText, settings);
            ResultLog actualLog = JsonConvert.DeserializeObject<ResultLog>(actualText, settings);

            var visitor = new ResultDiffingVisitor(expectedLog);

            if (!visitor.Diff(actualLog.RunLogs[0].Results))
            {
                string errorMessage = "The output of the tool did not match for input {0}.";
                sb.AppendLine(String.Format(CultureInfo.CurrentCulture, errorMessage, inputFileName));
                sb.AppendLine("Check differences with:");
                sb.AppendLine(GenerateDiffCommand(expectedFileName, actualFileName));
            }
        }

        private string GenerateDiffCommand(string expected, string actual)
        {
            expected = Path.GetFullPath(expected);
            actual = Path.GetFullPath(actual);

            string beyondCompare = TryFindBeyondCompare();
            if (beyondCompare != null)
            {
                return String.Format(CultureInfo.InvariantCulture, "\"{0}\" \"{1}\" \"{2}\" /title1=Expected /title2=Actual", beyondCompare, expected, actual);
            }

            return String.Format(CultureInfo.InvariantCulture, "tfsodd \"{0}\" \"{1}\"", expected, actual);
        }

        private static string TryFindBeyondCompare()
        {
            List<string> directories = new List<string>();
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            directories.Add(programFiles);
            directories.Add(programFiles.Replace(" (x86)", ""));

            foreach (string directory in directories)
            {
                for (int idx = 4; idx >= 3; --idx)
                {
                    string beyondComparePath = String.Format(CultureInfo.InvariantCulture, "{0}\\Beyond Compare {1}\\BComp.exe", directory, idx);
                    if (File.Exists(beyondComparePath))
                    {
                        return beyondComparePath;
                    }
                }

                string beyondCompare2Path = programFiles + "\\Beyond Compare 2\\BC2.exe";
                if (File.Exists(beyondCompare2Path))
                {
                    return beyondCompare2Path;
                }
            }

            return null;
        }
    }
}
