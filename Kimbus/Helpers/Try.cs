using System;

namespace Kimbus.Helpers
{
    public static class Try
    {
        public static Try<T> Apply<T>(Func<T> throwableFunc)
        {
            return new Try<T>(throwableFunc);
        }

        public static Try<bool> Apply(Action throwableAction)
        {
            return new Try<bool>(() =>
            {
                throwableAction();
                return false;
            });
        }

        public static Try<T> Success<T>(T val)
        {
            return new Try<T>(() => val);
        }

        public static Try<T> Failure<T>(Exception ex)
        {
            return new Try<T>(() => { throw ex; });
        }
    }

    public class Try<T>
    {
        public bool IsFailure { get; }

        public bool IsSuccess { get { return !IsFailure; } }

        public T Success { get; }

        public Exception Failure { get; }

        public Try(Func<T> throwableFunc)
        {
            try
            {
                Success = throwableFunc();
                IsFailure = false;
            }
            catch (Exception e)
            {
                // unwrap aggregate exception, use first
                // FIXME: what to do with aggregate exceptions?
                if (e is AggregateException)
                {
                    e = e.InnerException;
                }
                Failure = e;
                IsFailure = true;
            }
        }

        public Try(Action throwableAction)
        {
            try
            {
                throwableAction();
                IsFailure = false;
            }
            catch (Exception e)
            {
                IsFailure = true;
                Failure = e;
            }
        }
    }

    public static class TryLinqExtensions
    {
        public static Try<TOut> Select<TIn, TOut>(
          this Try<TIn> tryin,
          Func<Try<TIn>, Try<TOut>> projection)
        {
            if (tryin.IsFailure) return Try.Failure<TOut>(tryin.Failure);
            return projection(tryin);
        }

        public static Try<TOut> SelectMany<TIn, TMid, TOut>(
          this Try<TIn> tryin,
          Func<TIn, Try<TMid>> midProjection,
          Func<TIn, TMid, TOut> outProjection)
        {
            if (tryin.IsFailure) return Try.Failure<TOut>(tryin.Failure);
            var trymid = midProjection(tryin.Success);
            if (trymid.IsFailure) return Try.Failure<TOut>(trymid.Failure);
            return Try.Apply(() => outProjection(tryin.Success, trymid.Success));
        }
    }
}
