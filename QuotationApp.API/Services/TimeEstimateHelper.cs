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

    public static List<TimeEstimateStageRequest> Defaults()
    {
        return Stages.Select(k => new TimeEstimateStageRequest
        {
            StageKey = k,
            StartWeek = DefaultsMap[k].StartWeek,
            EndWeek = DefaultsMap[k].EndWeek
        }).ToList();
    }

    public static void Validate(List<TimeEstimateStageRequest> list)
    {
        if (list is null || list.Count == 0)
            throw new ArgumentException("Time estimate is required for all 7 stages.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stage in list)
        {
            if (string.IsNullOrWhiteSpace(stage.StageKey))
                throw new ArgumentException("StageKey is required for every time estimate stage.");

            var key = stage.StageKey.Trim().ToLowerInvariant();
            if (!Array.Exists(Stages, s => s.Equals(key, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"Unknown stage '{stage.StageKey}'. Valid stages: {string.Join(", ", Stages)}.");

            if (!seen.Add(key))
                throw new ArgumentException($"Duplicate stage '{stage.StageKey}'.");

            if (stage.StartWeek < 1 || stage.StartWeek > TotalWeeks)
                throw new ArgumentException($"Stage '{stage.StageKey}': weeks must be between 1 and 8 and 'From' cannot be after 'To'. The time estimate is limited to 2 months.");

            if (stage.EndWeek < 1 || stage.EndWeek > TotalWeeks)
                throw new ArgumentException($"Stage '{stage.StageKey}': weeks must be between 1 and 8 and 'From' cannot be after 'To'. The time estimate is limited to 2 months.");

            if (stage.StartWeek > stage.EndWeek)
                throw new ArgumentException($"Stage '{stage.StageKey}': weeks must be between 1 and 8 and 'From' cannot be after 'To'. The time estimate is limited to 2 months.");
        }

        var missing = Stages.Where(s => !seen.Contains(s)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Missing time estimate for stage(s): {string.Join(", ", missing)}.");
    }
}