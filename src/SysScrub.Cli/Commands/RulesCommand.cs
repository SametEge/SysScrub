using SysScrub.Core.Rules;

namespace SysScrub.Cli.Commands;

internal static class RulesCommand
{
    public static int Run(string[] args)
    {
        bool verbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

        RuleSet ruleSet = new RuleLoader().Load();

        foreach (RuleCategoryGroup category in ruleSet.GroupForDisplay())
        {
            Console.WriteLine();
            Console.WriteLine(category.Category.ToString().ToUpperInvariant());

            foreach (RuleGroup group in category.Groups)
            {
                Console.WriteLine($"  {group.Name}");

                foreach (CleaningRule rule in group.Rules)
                {
                    string flags = string.Concat(
                        rule.DefaultEnabled ? "[x] " : "[ ] ",
                        rule.RequiresAdmin ? "yönetici " : string.Empty,
                        rule.Risk == RiskLevel.Safe ? string.Empty : rule.Risk.ToString().ToLowerInvariant() + " ");

                    Console.WriteLine($"    {flags}{rule.Name.Resolve()}  ({rule.Id})");

                    if (verbose)
                    {
                        foreach (RuleRoot root in rule.Roots)
                        {
                            Console.WriteLine($"          {root}");
                        }
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Toplam {ruleSet.Rules.Count} kural.");

        if (ruleSet.Issues.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Yüklenemeyen kurallar:");

            foreach (RuleIssue issue in ruleSet.Issues)
            {
                Console.WriteLine($"  {issue}");
            }
        }

        return 0;
    }
}
