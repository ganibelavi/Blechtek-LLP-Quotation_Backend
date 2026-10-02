using System;
using System.Collections.Generic;
using System.Linq;
using QuotationApp.API.Models;

namespace QuotationApp.API.Services;

public static class TimeEstimateHelper
{
    public const int TotalWeeks = 8;

    public static readonly string[] Stages =
    {
        "pre", "master", "config", "train", "data", "golive", "support"
    };

    public static readonly Dictionary<string, (int StartWeek, int EndWeek)> DefaultsMap = new()
    {
        ["pre"] = (1, 1),
        ["master"] = (2, 2),
        ["config"] = (3, 4),
        ["train"] = (4, 4),
        ["data"] = (5, 5),
        ["golive"] = (6, 6),
        ["support"] = (7, 7)
    };

    private static readonly Dictionary<string, string> StageLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pre"] = "Pre-Implementation Visits",
        ["master"] = "Master Preparations",
        ["config"] = "Configuration & Set Up",
        ["train"] = "Trainings and Pilot run",
        ["data"] = "Data Preparation for Go-Live",
        ["golive"] = "Go-Live",
        ["support"] = "Go-Live Support"
    };

    public static List<TimeEstimateStageRequest> Defaults(IEnumerable<string> selectedModules)
    {
        return selectedModules
            .SelectMany(moduleName => Stages.Select(stageKey => new TimeEstimateStageRequest
            {
                ModuleName = moduleName,
                StageKey = stageKey,
                StartWeek = DefaultsMap[stageKey].StartWeek,
                EndWeek = DefaultsMap[stageKey].EndWeek
            }))
            .ToList();
    }

    public static string GetStageLabel(string stageKey)
    {
        return StageLabels[stageKey];
    }

    public static List<TimeEstimateStageRequest> PrepareForModules(
        List<TimeEstimateStageRequest>? list,
        IReadOnlyCollection<string> selectedModules)
    {
        if (selectedModules.Count == 0)
            throw new ArgumentException("Select at least one module before setting a time estimate.");

        if (selectedModules.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Module names cannot be empty in a time estimate.");

        if (selectedModules.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selectedModules.Count)
            throw new ArgumentException("A module cannot be selected more than once in a time estimate.");

        if (list is null || list.Count == 0)
            return Defaults(selectedModules);

        if (list.All(stage => string.IsNullOrWhiteSpace(stage.ModuleName)))
        {
            ValidateStageSet(list, "quotation");
            return selectedModules
                .SelectMany(moduleName => list.Select(stage => new TimeEstimateStageRequest
                {
                    ModuleName = moduleName,
                    StageKey = stage.StageKey,
                    StartWeek = stage.StartWeek,
                    EndWeek = stage.EndWeek
                }))
                .ToList();
        }

        var canonicalModules = selectedModules
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(moduleName => moduleName, moduleName => moduleName, StringComparer.OrdinalIgnoreCase);

        var stagesByModule = new Dictionary<string, List<TimeEstimateStageRequest>>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in list)
        {
            if (string.IsNullOrWhiteSpace(stage.ModuleName))
                throw new ArgumentException("ModuleName is required when time estimates are supplied per module.");

            if (!canonicalModules.TryGetValue(stage.ModuleName.Trim(), out var canonicalModuleName))
                throw new ArgumentException($"Time estimate module '{stage.ModuleName}' is not selected for this quotation.");

            stage.ModuleName = canonicalModuleName;
            if (!stagesByModule.TryGetValue(canonicalModuleName, out var moduleStages))
            {
                moduleStages = new List<TimeEstimateStageRequest>();
                stagesByModule.Add(canonicalModuleName, moduleStages);
            }

            moduleStages.Add(stage);
        }

        foreach (var moduleName in selectedModules)
        {
            if (!stagesByModule.TryGetValue(moduleName, out var moduleStages))
                throw new ArgumentException($"Time estimate is required for selected module '{moduleName}'.");

            ValidateStageSet(moduleStages, moduleName);
        }

        return list;
    }

    private static void ValidateStageSet(List<TimeEstimateStageRequest> stages, string moduleName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in stages)
        {
            if (string.IsNullOrWhiteSpace(stage.StageKey))
                throw new ArgumentException($"StageKey is required for every time estimate stage for module '{moduleName}'.");

            var key = stage.StageKey.Trim().ToLowerInvariant();
            if (!Array.Exists(Stages, s => s.Equals(key, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Unknown stage '{stage.StageKey}'. Valid stages: {string.Join(", ", Stages)}.");

            stage.StageKey = key;
            if (!seen.Add(key))
                throw new ArgumentException($"Duplicate stage '{stage.StageKey}' for module '{moduleName}'.");

            if (stage.StartWeek < 1 || stage.StartWeek > TotalWeeks)
                throw new ArgumentException($"Module '{moduleName}', stage '{stage.StageKey}': weeks must be between 1 and 8 and 'From' cannot be after 'To'. The time estimate is limited to 2 months.");

            if (stage.EndWeek < 1 || stage.EndWeek > TotalWeeks)
                throw new ArgumentException($"Module '{moduleName}', stage '{stage.StageKey}': weeks must be between 1 and 8 and 'From' cannot be after 'To'. The time estimate is limited to 2 months.");

            if (stage.StartWeek > stage.EndWeek)
                throw new ArgumentException($"Module '{moduleName}', stage '{stage.StageKey}': weeks must be between 1 and 8 and 'From' cannot be after 'To'. The time estimate is limited to 2 months.");
        }

        var missing = Stages.Where(stageKey => !seen.Contains(stageKey)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Missing time estimate stage(s) for module '{moduleName}': {string.Join(", ", missing)}.");
    }
}