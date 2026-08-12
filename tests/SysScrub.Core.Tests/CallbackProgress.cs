namespace SysScrub.Core.Tests;

/// <summary>
/// Geri çağırmayı doğrudan raporlayan iş parçacığında çalıştıran IProgress.
///
/// Progress&lt;T&gt; geri çağırmaları yakaladığı eşzamanlama bağlamına gönderiyor;
/// testlerde işin hangi iş parçacığında yürüdüğünü ölçmek için bu gerekiyor.
/// </summary>
internal sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
