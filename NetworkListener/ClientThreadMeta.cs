namespace NetworkListenerCore
{
    using System.Diagnostics.CodeAnalysis;
    using System.Security.Authentication;
    using System.Threading;

    /// <summary>
    /// Client thread meta-data
    /// </summary>
    internal struct ClientThreadMeta
    {
        /// <summary>
        /// Client thread name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Client thread
        /// </summary>
        public Thread Thread { get; set; }

        /// <summary>
        /// Cancellation token for client thread cancellation
        /// </summary>
        public CancellationTokenSource CancellationTokenSource { get; set; }

        /// <summary>
        /// None static client thread meta
        /// </summary>
        public static ClientThreadMeta None = new()
        {
            Name = "[None]-[1DA82AA4-9BDB-43FA-B1AF-0D754FD5CCF1]",
            Thread = null!,
            CancellationTokenSource = null!
        };

        public static bool operator ==(ClientThreadMeta left, ClientThreadMeta right)
        {
            return left.Name == right.Name;
        }

        public static bool operator !=(ClientThreadMeta left, ClientThreadMeta right)
        {
            return left.Name != right.Name;
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is null)
            {
                return false;
            }

            return Name.Equals(((ClientThreadMeta)obj).Name);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}
