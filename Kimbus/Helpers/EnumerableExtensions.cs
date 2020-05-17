using System.Collections.Generic;

namespace Kimbus.Helpers
{
    internal static class EnumerableExtensions
    {
        public static IEnumerable<IEnumerable<T>> Chunk<T>(
            this IEnumerable<T> source, int chunkSize)
        {
            using (var enumerator = source.GetEnumerator())
                while (enumerator.MoveNext())
                    yield return YieldChunkElements(enumerator, chunkSize - 1);
        }

        private static IEnumerable<T> YieldChunkElements<T>(
            IEnumerator<T> source, int chunkSize)
        {
            yield return source.Current;
            for (int i = 0; i < chunkSize && source.MoveNext(); i++)
                yield return source.Current;
        }
    }
}
