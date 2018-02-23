package org.mydotey.objectpool.threadpool;

import java.io.IOException;
import java.util.Objects;

import org.mydotey.objectpool.ObjectPool;
import org.mydotey.objectpool.ObjectPoolConfig;
import org.mydotey.objectpool.ObjectPool.Entry;
import org.mydotey.objectpool.facade.ObjectPools;

/**
 * @author koqizhao
 *
 * Feb 6, 2018
 */
public class DefaultThreadPool implements ThreadPool {

    private ObjectPool<WorkerThread> _objectPool;

    public DefaultThreadPool(ThreadPoolConfig.Builder builder) {
        _objectPool = newObjectPool(builder);
    }

    protected ObjectPool<WorkerThread> newObjectPool(ThreadPoolConfig.Builder builder) {
        ObjectPoolConfig<WorkerThread> config = ((DefaultThreadPoolConfig.Builder) builder).setThreadPool(this).build();
        return ObjectPools.newObjectPool(config);
    }

    protected ObjectPool<WorkerThread> getObjectPool() {
        return _objectPool;
    }

    @Override
    public int getSize() {
        return getObjectPool().getSize();
    }

    @Override
    public void submit(Runnable task) throws InterruptedException {
        Objects.requireNonNull(task, "task is null");

        Entry<WorkerThread> entry = getObjectPool().acquire();
        entry.getObject().setTask(task);
    }

    @Override
    public boolean trySubmit(Runnable task) {
        Objects.requireNonNull(task, "task is null");

        Entry<WorkerThread> entry = getObjectPool().tryAcquire();
        if (entry == null)
            return false;

        entry.getObject().setTask(task);
        return true;
    }

    @Override
    public void close() throws IOException {
        getObjectPool().close();
    }
}
