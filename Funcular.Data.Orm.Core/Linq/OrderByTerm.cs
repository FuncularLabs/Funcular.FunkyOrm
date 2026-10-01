namespace Funcular.Data.Orm.Linq
{
    /// <summary>
    /// One term of a translated ORDER BY: the SQL fragment and its direction.
    /// </summary>
    public readonly struct OrderByTerm
    {
        /// <summary>
        /// Initializes a new <see cref="OrderByTerm"/>.
        /// </summary>
        public OrderByTerm(string fragment, bool isDescending)
        {
            Fragment = fragment;
            IsDescending = isDescending;
        }

        /// <summary>
        /// The SQL fragment ordered by (a column, a qualified column, a resolved remote fragment, or a CASE).
        /// </summary>
        public string Fragment { get; }

        /// <summary>
        /// Whether the term sorts descending.
        /// </summary>
        public bool IsDescending { get; }
    }
}
