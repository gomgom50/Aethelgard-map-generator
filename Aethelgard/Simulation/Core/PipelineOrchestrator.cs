using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Aethelgard.Simulation.Core
{
    /// <summary>
    /// Manages the execution of the world generation pipeline.
    /// Handles stage sequencing, progress tracking, and pausing/stepping.
    /// </summary>
    public class PipelineOrchestrator
    {
        private WorldMap _map;
        private ConstraintManager _constraintManager;
        private readonly List<IGenerationStage> _stages = new();

        // Exposed state for UI
        public IReadOnlyList<IGenerationStage> Stages => _stages;
        public int CurrentStageIndex { get; private set; } = 0;
        public bool IsRunning { get; private set; } = false;
        public string GlobalStatus { get; private set; } = "Ready";

        public event Action? OnStageCompleted;

        public PipelineOrchestrator(WorldMap map, ConstraintManager constraintManager)
        {
            _map = map;
            _constraintManager = constraintManager;
        }

        public void UpdateMap(WorldMap map, ConstraintManager constraintManager)
        {
            _map = map;
            _constraintManager = constraintManager;
            Reset(); // Reset pipeline when map changes
        }

        public void RegisterStage(IGenerationStage stage)
        {
            _stages.Add(stage);
        }

        public void Reset()
        {
            CurrentStageIndex = 0;
            IsRunning = false;
            GlobalStatus = "Ready";
        }

        /// <summary>
        /// Runs all remaining stages from current index to the end.
        /// </summary>
        public async Task RunAll()
        {
            if (IsRunning) return;
            IsRunning = true;
            GlobalStatus = "Running Pipeline...";

            try
            {
                while (CurrentStageIndex < _stages.Count)
                {
                    await ExecuteCurrentStage();
                }

                GlobalStatus = "Pipeline Complete";
            }
            catch (Exception)
            {
                GlobalStatus = "Pipeline Failed";
                throw;
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// Executes the next single stage in the pipeline.
        /// </summary>
        public async Task Step()
        {
            if (IsRunning) return;
            if (CurrentStageIndex >= _stages.Count) return;

            IsRunning = true;
            GlobalStatus = $"Stepping Stage {CurrentStageIndex + 1}";

            try
            {
                await ExecuteCurrentStage();

                if (CurrentStageIndex >= _stages.Count)
                    GlobalStatus = "Pipeline Complete";
                else
                    GlobalStatus = "Paused";
            }
            catch (Exception)
            {
                GlobalStatus = "Stage Failed";
                throw;
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// Internal execution of the stage at CurrentStageIndex.
        /// Increments index upon success.
        /// </summary>
        private Task ExecuteCurrentStage()
        {
            var stage = _stages[CurrentStageIndex];

            // Run on background thread
            return Task.Run(() =>
            {
                try
                {
                    Console.WriteLine($"[Pipeline] Starting stage: {stage.Name}");
                    var stopwatch = Stopwatch.StartNew();

                    stage.Execute(_map, _constraintManager);

                    stopwatch.Stop();
                    Console.WriteLine($"[Pipeline] Completed stage: {stage.Name} in {stopwatch.ElapsedMilliseconds}ms");

                    CurrentStageIndex++;
                    OnStageCompleted?.Invoke();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Pipeline] ERROR in stage {stage.Name}: {ex}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Runs a specific named stage individually (e.g. for "Regenerate Tectonics" button).
        /// </summary>
        public Task RunStage(string stageName)
        {
            var stage = _stages.Find(s => s.Name.Equals(stageName, StringComparison.OrdinalIgnoreCase));
            if (stage != null)
            {
                // We run it directly without modifying CurrentStageIndex, 
                // effectively treating it as an "Undo/Redo" or "Tweak" operation.
                return Task.Run(() =>
                {
                    try
                    {
                        stage.Execute(_map, _constraintManager);
                        OnStageCompleted?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Pipeline] Error in targeted execution: {ex}");
                    }
                });
            }
            return Task.CompletedTask;
        }
    }
}
