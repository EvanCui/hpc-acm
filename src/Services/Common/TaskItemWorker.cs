﻿namespace Microsoft.HpcAcm.Services.Common
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public abstract class TaskItemWorker : ServerObject, IWorker
    {
        protected TaskItemWorker(TaskItemSourceOptions taskItemSourceOptions)
        {
            this.TaskItemSourceOptions = taskItemSourceOptions;
        }

        protected ITaskItemSource Source { get; set; }
        private TaskItemSourceOptions TaskItemSourceOptions { get; }

        protected bool shouldExit = false;

        public virtual Task InitializeAsync(CancellationToken token)
        {
            if (this.Source is ServerObject so)
            {
                so.CopyFrom(this);
            }

            return Task.CompletedTask;
        }

        public async Task DoWorkAsync(CancellationToken token)
        {
            using (this.Source as IDisposable)
            {
                while (!token.IsCancellationRequested && !this.shouldExit)
                {
                    try
                    {
                        using (var taskItem = await this.Source.FetchTaskItemAsync(token))
                        {
                            if (taskItem == null)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(this.TaskItemSourceOptions.RetryIntervalSeconds), token);
                                continue;
                            }

                            var success = await this.ProcessTaskItemAsync(taskItem, taskItem.Token);
                            if (token.IsCancellationRequested) break;
                            await (success ? taskItem.FinishAsync(token) : taskItem.ReturnAsync(token));
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                    catch (Exception ex)
                    {
                        this.Logger.Error(ex, $"Exception happened in {nameof(DoWorkAsync)}");
                        await Task.Delay(TimeSpan.FromSeconds(this.TaskItemSourceOptions.FailureRetryIntervalSeconds), token);
                    }
                }
            }

            this.Logger.Information($"Exiting {this.GetType().Name}.{nameof(DoWorkAsync)}, canceled {0}", token.IsCancellationRequested);
        }

        public abstract Task<bool> ProcessTaskItemAsync(TaskItem taskItem, CancellationToken token);
    }
}
