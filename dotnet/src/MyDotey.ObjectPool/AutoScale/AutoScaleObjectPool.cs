using System;
using System.Collections.Concurrent;
using System.Threading;
using NLog;
using MyDotey.ObjectPool.ThreadPool;
using MyDotey.ObjectPool.Facade;

/**
 * @author koqizhao}
 *
 * Feb 23, 2018
 */
namespace MyDotey.ObjectPool.AutoScale
{
    public class AutoScaleObjectPool<T> : ObjectPool<T>, IAutoScaleObjectPool<T>
    {
        private static ILogger _logger = LogManager.GetCurrentClassLogger();

        protected internal static long CurrentTimeMillis { get { return DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond; } }

        protected Thread _taskScheduler;

        protected volatile int _scalingOut;
        protected Action _scaleOutTask;

        protected IThreadPool _taskExecutor;

        public new virtual IAutoScaleObjectPoolConfig<T> Config { get { return (AutoScaleObjectPoolConfig<T>)base.Config; } }

        public AutoScaleObjectPool(IAutoScaleObjectPoolConfig<T> config)
            : base(config)
        {

        }

        protected override void Init()
        {
            base.Init();

            _taskScheduler = new Thread(AutoCheck)
            {
                IsBackground = true
            };
            _taskScheduler.Start();

            _scaleOutTask = () =>
            {
                try
                {
                    TryAddNewEntry(Config.ScaleFactor - 1);
                    _logger.Info("scaleOut success");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "scaleOut failed");
                }
                finally
                {
                    Interlocked.CompareExchange(ref _scalingOut, 1, 0);
                }
            };

            ThreadPool.IBuilder builder = ThreadPools.NewThreadPoolConfigBuilder();
            builder.SetMinSize(1).SetMaxSize(1);
            _taskExecutor = ThreadPools.NewThreadPool(builder.Build());
        }

        protected override ObjectPool<T>.Entry TryAddNewEntryAndAcquireOne()
        {
            ObjectPool<T>.Entry entry = TryCreateNewEntry();
            if (entry == null)
                return null;

            if (Interlocked.CompareExchange(ref _scalingOut, 0, 1) == 0)
                SubmitTaskSafe(_scaleOutTask);

            return base.DoAcquire(entry);
        }

        protected override void AddNewEntry(ObjectPool<T>.Entry entry)
        {
            lock (entry.Key)
            {
                base.AddNewEntry(entry);
            }
        }

        protected new virtual AutoScaleEntry NewPoolEntry(Object key)
        {
            return (AutoScaleEntry)base.NewPoolEntry(key);
        }

        protected override ObjectPool<T>.Entry NewConcretePoolEntry(Object key, T obj)
        {
            return new AutoScaleEntry(key, obj);
        }

        protected new virtual AutoScaleEntry GetEntry(Object key)
        {
            return (AutoScaleEntry)base.GetEntry(key);
        }

        protected override ObjectPool<T>.Entry TryAcquire(Object key)
        {
            ObjectPool<T>.Entry entry = DoAcquire(key);
            if (entry != null)
                return entry;

            return (ObjectPool<T>.Entry)TryAcquire();
        }

        protected override ObjectPool<T>.Entry Acquire(Object key)
        {
            ObjectPool<T>.Entry entry = DoAcquire(key);
            if (entry != null)
                return entry;

            return (ObjectPool<T>.Entry)Acquire();
        }

        protected override ObjectPool<T>.Entry DoAcquire(Object key)
        {
            lock (key)
            {
                AutoScaleEntry entry = GetEntry(key);
                if (entry == null)
                    return null;

                base.DoAcquire(entry);

                if (!NeedRefresh(entry))
                {
                    entry.Renew();
                    return entry;
                }
                else
                {
                    entry.Status = AutoScaleEntry.EntryStatus.PendingRefresh;
                }
            }

            ReleaseKey(key);
            return null;
        }

        protected override void ReleaseKey(Object key)
        {
            SubmitTaskSafe(() => DoReleaseKey(key));
        }

        protected virtual void DoReleaseKey(Object key)
        {
            lock (key)
            {
                AutoScaleEntry entry = GetEntry(key);
                if (entry.Status == AutoScaleEntry.EntryStatus.PendingRefresh)
                {
                    if (!TryRefresh(entry))
                    {
                        ScaleIn(entry);
                        return;
                    }
                }
                else
                    entry.Renew();

                base.ReleaseKey(key);
            }
        }

        protected virtual void AutoCheck()
        {
            while (true)
            {
                try
                {
                    Thread.Sleep((int)Config.CheckInterval);
                }
                catch
                {
                    break;
                }

                foreach (Object key in _entries.Keys)
                {
                    if (TryScaleIn(key))
                        continue;

                    TryRefresh(key);
                }
            }
        }

        protected virtual bool TryScaleIn(Object key)
        {
            AutoScaleEntry entry = GetEntry(key);
            if (!NeedScaleIn(entry))
                return false;

            lock (key)
            {
                entry = GetEntry(key);
                if (!NeedScaleIn(entry))
                    return false;

                ScaleIn(entry);
                return true;
            }
        }

        protected virtual void ScaleIn(AutoScaleEntry entry)
        {
            lock (AddLock)
            {
                _entries.TryRemove(entry.Key, out IEntry<T> value);
                Close(entry);
                _logger.Info("scaled in an object: {0}", entry.Object);
            }
        }

        protected virtual bool TryRefresh(Object key)
        {
            AutoScaleEntry entry = GetEntry(key);
            if (!NeedRefresh(entry))
                return false;

            lock (key)
            {
                entry = GetEntry(key);
                if (!NeedRefresh(entry))
                    return false;

                if (entry.Status == AutoScaleEntry.EntryStatus.Available)
                    return TryRefresh(entry);

                entry.Status = AutoScaleEntry.EntryStatus.PendingRefresh;
                return false;
            }
        }

        protected virtual bool TryRefresh(AutoScaleEntry entry)
        {
            AutoScaleEntry newEntry = null;
            try
            {
                newEntry = NewPoolEntry(entry.Key);
            }
            catch (Exception e)
            {
                _logger.Error(e, "failed to refresh object: {0}, still use it", entry.Object);
                return false;
            }

            Close(entry);
            _entries.TryAdd(entry.Key, newEntry);

            _logger.Info("refreshed an object, old: {0}, new: {1}", entry.Object, newEntry.Object);
            return true;
        }

        protected virtual bool NeedRefresh(AutoScaleEntry entry)
        {
            return IsExpired(entry) || IsStale(entry);
        }

        protected virtual bool IsExpired(AutoScaleEntry entry)
        {
            return entry.CreationTime <= CurrentTimeMillis - Config.ObjectTtl;
        }

        protected virtual bool IsStale(AutoScaleEntry entry)
        {
            try
            {
                return Config.StaleChecker(entry.Object);
            }
            catch (Exception e)
            {
                _logger.Error(e, "staleChecker failed, ignore");
                return false;
            }
        }

        protected virtual bool NeedScaleIn(AutoScaleEntry entry)
        {
            return entry.Status == AutoScaleEntry.EntryStatus.Available
                    && entry.LastUsedTime <= CurrentTimeMillis - Config.MaxIdleTime
                    && Size > Config.MinSize;
        }

        protected override void DoClose()
        {
            base.DoClose();

            try
            {
                _taskScheduler.Interrupt();
                _taskExecutor.Dispose();
            }
            catch (Exception e)
            {
                _logger.Error(e, "shutdown timer failed.");
            }
        }

        protected virtual void SubmitTaskSafe(Action task)
        {
            try
            {
                _taskExecutor.Submit(task);
            }
            catch
            {
                task();
            }
        }

        protected internal class AutoScaleEntry : ObjectPool<T>.Entry
        {
            public new class EntryStatus : ObjectPool<T>.Entry.EntryStatus
            {
                public const String PendingRefresh = "pending_refresh";
            }

            public virtual long CreationTime { get; }
            public virtual long LastUsedTime { get; set; }

            public AutoScaleEntry(Object key, T obj)
                : base(key, obj)
            {
                CreationTime = CurrentTimeMillis;
                LastUsedTime = CreationTime;
            }

            public virtual void Renew()
            {
                LastUsedTime = CurrentTimeMillis;
            }
        }
    }
}