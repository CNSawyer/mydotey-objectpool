mydotey objectpool java
================

## ObjectPool
```
ObjectPoolConfig.Builder<Object> builder = ObjectPools.newObjectPoolConfigBuilder();
builder.setMaxSize(10))
    .setMinSize(1)
    .setObjectFactory(() -> new Object());
ObjectPoolConfig<Object> config = builder.build();
ObjectPool objectPool = ObjectPools.newObjectPool(config);

Entry<Object> entry = null;
try {
    entry = objectPool.acquire();
    Object o = entry.getObject();
    // use object
} finally {
    if (entry != null)
        objectPool.release(entry);
}
```

## AutoScaleObjectPool
```
AutoScaleObjectPoolConfig.Builder<WorkerThread> builder = ObjectPools.newAutoScaleObjectPoolConfigBuilder();
builder.setMaxSize(100))
    .setMinSize(10)
    .setObjectFactory(() -> new Object())
    .setCheckInterval(5 * 1000))
    .setObjectTtl(5 * 60 * 1000)
    .setMaxIdleTime(1 * 60 * 1000)
    .setScaleFactor(5)
    .setStaleChecker(o -> false);
AutoScaleObjectPoolConfig<WorkerThread> config = builder.build();
AutoScaleObjectPool<WorkerThread> objectPool = ObjectPools.newAutoScaleObjectPool(config);

Entry<Object> entry = null;
try {
    entry = objectPool.acquire();
    Object o = entry.getObject();
    // use object
} finally {
    if (entry != null)
        objectPool.release(entry);
}
```

## ThreadPool
```
ThreadPoolConfig.Builder builder = ThreadPools.newThreadPoolConfigBuilder();
builder.setMinSize(1)
    .setMaxSize(10)
    .setQueueCapacity(100);
ThreadPoolConfig config = builder.build();
ThreadPool threadPool = ThreadPools.newThreadPool(config);

threadPool.submit(() -> System.out.println("Hello, world!"));
```

## AutoScaleThreadPool
```
AutoScaleThreadPoolConfig.Builder builder = ThreadPools.newAutoScaleThreadPoolConfigBuilder();
builder.setMinSize(10)
    .setMaxSize(100)
    .setScaleFactor(5)
    .setCheckInterval(5 * 1000)
    .setMaxIdleTime(1 * 60 * 1000)
    .setQueueCapacity(1000);
AutoScaleThreadPoolConfig config = builder.build();
AutoScaleThreadPool threadPool = ThreadPools.newAutoScaleThreadPool(config);

threadPool.submit(() -> System.out.println("Hello, world!"));
```