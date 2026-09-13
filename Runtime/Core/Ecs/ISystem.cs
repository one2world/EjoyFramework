namespace EjoyFramework.Core.Ecs
{
    /// <summary>系统仅处理数据；结构变更记录到命令缓冲。系统实例由调用方持有。</summary>
    public interface ISystem
    {
        void Update(World world, CommandBuffer commands, float deltaTime);
    }
}
