﻿using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using Microsoft.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Primitives;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.OData.UriParser.Aggregation;
using QueryBuilder.Abstracts;
using QueryBuilder.Edm;
using QueryBuilder.Query.Container;
using QueryBuilder.Routing;
using QueryBuilder.Query.Validator;
using Microsoft.OData.ModelBuilder;

namespace QueryBuilder.Query
{
    /// <summary>
    /// This defines a composite OData query options that can be used to perform query composition.
    /// Currently this only supports $filter, $orderby, $top, $skip, and $count.
    /// </summary>
    // TODO: Fix attribute compilation below:
    //[NonValidatingParameterBinding]
    //[ODataQueryParameterBinding]
    [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling", Justification = "Relies on many ODataLib classes.")]
    public class ODataQueryOptionsFundamentals
    {
        private static readonly MethodInfo _limitResultsGenericMethod = typeof(ODataQueryOptionsFundamentals).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(mi => mi.Name == "LimitResults" && mi.ContainsGenericParameters && mi.GetParameters().Length == 4);

        private ODataQueryOptionParser _queryOptionParser;

        private ETag _etagIfMatch;

        private bool _etagIfMatchChecked;

        private ETag _etagIfNoneMatch;

        private bool _etagIfNoneMatchChecked;

        private bool _enableNoDollarSignQueryOptions = false;

        private OrderByQueryOption _stableOrderBy;

        /// <summary>
        /// Initializes a new instance of the <see cref="ODataQueryOptionsFundamentals"/> class based on the incoming request and some metadata information from
        /// the <see cref="ODataQueryFundamentalsContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="ODataQueryFundamentalsContext"/> which contains the <see cref="IEdmModel"/> and some type information.</param>
        /// <param name="request">The incoming request message.</param>
        public ODataQueryOptionsFundamentals(ODataQueryFundamentalsContext context, IEnumerable<KeyValuePair<string, StringValues>> requestQueryCollection)
        {
            if (context == null)
            {
                throw Error.ArgumentNull(nameof(context));
            }

            if (requestQueryCollection == null)
            {
                throw Error.ArgumentNullOrEmpty(nameof(requestQueryCollection));
            }

            context.RequestQueryCollection = requestQueryCollection;

            QueryContext = context;
            RequestQueryCollection = requestQueryCollection;

            Initialize(context);
        }

        /// <summary>
        /// Gets the request message associated with this instance.
        /// </summary>
        public IEnumerable<KeyValuePair<string, StringValues>> RequestQueryCollection { get; private set; }

        /// <summary>
        ///  Gets the given <see cref="ODataQueryFundamentalsContext"/>
        /// </summary>
        public ODataQueryFundamentalsContext QueryContext { get; private set; }

        /// <summary>
        /// Gets the raw string of all the OData query options
        /// </summary>
        public ODataRawQueryOptions RawValues { get; private set; }

        /// <summary>
        /// Gets the <see cref="SelectExpandQueryOption"/>.
        /// </summary>
        public SelectExpandQueryOption SelectExpand { get; private set; }

        /// <summary>
        /// Gets the <see cref="ApplyQueryOption"/>.
        /// </summary>
        public ApplyQueryOption Apply { get; private set; }

        /// <summary>
        /// Gets the <see cref="ComputeQueryOption"/>.
        /// </summary>
        public ComputeQueryOption Compute { get; private set; }

        /// <summary>
        /// Gets the <see cref="FilterQueryOption"/>.
        /// </summary>
        public FilterQueryOption Filter { get; private set; }

        /// <summary>
        /// Gets the <see cref="SearchQueryOption"/>.
        /// </summary>
        public SearchQueryOption Search { get; private set; }

        /// <summary>
        /// Gets the <see cref="OrderByQueryOption"/>.
        /// </summary>
        public OrderByQueryOption OrderBy { get; private set; }

        /// <summary>
        /// Gets the <see cref="SkipQueryOption"/>.
        /// </summary>
        public SkipQueryOption Skip { get; private set; }

        /// <summary>
        /// Gets the <see cref="SkipTokenQueryOption"/>.
        /// </summary>
        public SkipTokenQueryOption SkipToken { get; private set; }

        /// <summary>
        /// Gets the <see cref="TopQueryOption"/>.
        /// </summary>
        public TopQueryOption Top { get; private set; }

        /// <summary>
        /// Gets the <see cref="CountQueryOption"/>.
        /// </summary>
        public CountQueryOption Count { get; private set; }

        /// <summary>
        /// Gets or sets the query validator.
        /// </summary>
        public IODataQueryValidator Validator { get; set; }

        /// <summary>
        /// Check if the given query option is an OData system query option using $-prefix-required theme.
        /// </summary>
        /// <param name="queryOptionName">The name of the query option.</param>
        /// <returns>Returns <c>true</c> if the query option is an OData system query option.</returns>
        public static bool IsSystemQueryOption(string queryOptionName)
        {
            return IsSystemQueryOption(queryOptionName, false);
        }

        /// <summary>
        /// Check if the given query option is an OData system query option.
        /// </summary>
        /// <param name="queryOptionName">The name of the query option.</param>
        /// <param name="isDollarSignOptional">Whether the optional-$-prefix scheme is used for OData system query.</param>
        /// <returns>Returns <c>true</c> if the query option is an OData system query option.</returns>
        public static bool IsSystemQueryOption(string queryOptionName, bool isDollarSignOptional)
        {
            if (string.IsNullOrEmpty(queryOptionName))
            {
                throw Error.ArgumentNullOrEmpty(nameof(queryOptionName));
            }

            string fixedQueryOptionName = queryOptionName;
            if (isDollarSignOptional && !queryOptionName.StartsWith("$", StringComparison.Ordinal))
            {
                fixedQueryOptionName = "$" + queryOptionName;
            }

            return fixedQueryOptionName.Equals("$orderby", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$filter", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$top", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$skip", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$count", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$expand", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$select", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$format", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$skiptoken", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$deltatoken", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$search", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$compute", StringComparison.Ordinal) ||
                 fixedQueryOptionName.Equals("$apply", StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the <see cref="ETag"/> from IfMatch header.
        /// </summary>
        public virtual ETag IfMatch(StringValues ifMatchValues, IETagHandler etagHandler)
        {
            if (!_etagIfMatchChecked && _etagIfMatch == null)
            {
                //StringValues ifMatchValues;
                //if (Request.Headers.TryGetValue("If-Match", out ifMatchValues))
                //{
                EntityTagHeaderValue etagHeaderValue = EntityTagHeaderValue.Parse(ifMatchValues.SingleOrDefault());
                _etagIfMatch = GetETag(etagHeaderValue, etagHandler);
                _etagIfMatchChecked = true;
                //}
            }

            return _etagIfMatch;
        }

        /// <summary>
        /// Gets the <see cref="ETag"/> from IfNoneMatch header.
        /// </summary>
        public virtual ETag IfNoneMatch(StringValues ifNoneMatchValues, IETagHandler etagHandler)
        {
            if (!_etagIfNoneMatchChecked && _etagIfNoneMatch == null)
            {
                //StringValues ifNoneMatchValues;
                //if (Request.Headers.TryGetValue("If-None-Match", out ifNoneMatchValues))
                //{
                EntityTagHeaderValue etagHeaderValue = EntityTagHeaderValue.Parse(ifNoneMatchValues.SingleOrDefault());
                _etagIfNoneMatch = GetETag(etagHeaderValue, etagHandler);
                if (_etagIfNoneMatch != null)
                {
                    _etagIfNoneMatch.IsIfNoneMatch = true;
                }
                _etagIfNoneMatchChecked = true;
                //}

                //_etagIfNoneMatchChecked = true;
            }

            return _etagIfNoneMatch;
        }

        /// <summary>
        /// Gets the EntityTagHeaderValue ETag.
        /// </summary>
        internal virtual ETag GetETag(EntityTagHeaderValue etagHeaderValue, IETagHandler etagHandler)
        {

            if (etagHeaderValue != null)
            {
                if (etagHeaderValue.Equals(EntityTagHeaderValue.Any))
                {
                    return new ETag { IsAny = true };
                }

                // get the etag handler, and parse the etag
                IDictionary<string, object> properties = etagHandler.ParseETag(etagHeaderValue) ?? new Dictionary<string, object>();
                IList<object> parsedETagValues = properties.Select(property => property.Value).ToList();

                // get property names from request
                ODataPath odataPath = QueryContext.Path;
                IEdmModel model = QueryContext.Model;
                IEdmNavigationSource source = odataPath.GetNavigationSource();
                if (model != null && source != null)
                {
                    IList<IEdmStructuralProperty> concurrencyProperties = model.GetConcurrencyProperties(source).ToList();
                    IList<string> concurrencyPropertyNames = concurrencyProperties.OrderBy(c => c.Name).Select(c => c.Name).ToList();
                    ETag etag = new ETag();

                    if (parsedETagValues.Count != concurrencyPropertyNames.Count)
                    {
                        etag.IsWellFormed = false;
                    }

                    IEnumerable<KeyValuePair<string, object>> nameValues = concurrencyPropertyNames.Zip(
                        parsedETagValues,
                        (name, value) => new KeyValuePair<string, object>(name, value));
                    foreach (var nameValue in nameValues)
                    {
                        IEdmStructuralProperty property = concurrencyProperties.SingleOrDefault(e => e.Name == nameValue.Key);
                        Contract.Assert(property != null);

                        Type clrType = model.GetClrType(property.Type);
                        Contract.Assert(clrType != null);

                        if (nameValue.Value != null)
                        {
                            Type valueType = nameValue.Value.GetType();
                            etag[nameValue.Key] = valueType != clrType
                                ? Convert.ChangeType(nameValue.Value, clrType, CultureInfo.InvariantCulture)
                                : nameValue.Value;
                        }
                        else
                        {
                            etag[nameValue.Key] = nameValue.Value;
                        }
                    }

                    return etag;
                }
            }

            return null;
        }

        /// <summary>
        /// Check if the given query option is the supported query option (case-insensitive by default).
        /// </summary>
        /// <param name="queryOptionName">The name of the query option.</param>
        /// <returns>Returns <c>true</c> if the query option is the supported query option.</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase",
            Justification = "Need lower case string here.")]
        public bool IsSupportedQueryOption(string queryOptionName)
        {
            if (string.IsNullOrEmpty(queryOptionName))
            {
                throw Error.ArgumentNullOrEmpty(nameof(queryOptionName));
            }

            string lowcaseQueryOptionName = queryOptionName.ToLowerInvariant();
            return IsSystemQueryOption(lowcaseQueryOptionName, this._enableNoDollarSignQueryOptions);
        }

        /// <summary>
        /// Check if the given query option is the supported query option.
        /// </summary>
        /// <param name="queryOptionName">The name of the query option.</param>
        /// <param name="enableCaseInsensitive">The setting to resolve with case-insensitivity.</param>
        /// <returns>Returns <c>true</c> if the query option is the supported query option.</returns>
        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase",
            Justification = "Need lower case string here.")]
        public bool IsSupportedQueryOption(string queryOptionName, bool enableCaseInsensitive)
        {
            if (string.IsNullOrEmpty(queryOptionName))
            {
                throw Error.ArgumentNullOrEmpty(nameof(queryOptionName));
            }

            // If we don't have the resolver setting, by default we support case-insensitive
            if (!enableCaseInsensitive)
            {
                return IsSystemQueryOption(queryOptionName, this._enableNoDollarSignQueryOptions);
            }

            string lowcaseQueryOptionName = queryOptionName.ToLowerInvariant();
            return IsSystemQueryOption(lowcaseQueryOptionName, this._enableNoDollarSignQueryOptions);
        }

        /// <summary>
        /// Apply the individual query to the given IQueryable in the right order.
        /// </summary>
        /// <param name="query">The original <see cref="IQueryable"/>.</param>
        /// <param name="querySettings">The settings to use in query composition.</param>
        /// <param name="ignoreQueryOptions">The query parameters that are already applied in queries.</param>
        /// <returns>The new <see cref="IQueryable"/> after the query has been applied to, as well as a <see cref="bool"/> representing whether the results have been limited to a page size.</returns>
        public virtual (IQueryable, bool) ApplyTo(IQueryable query, ODataQuerySettings querySettings, AllowedQueryOptions ignoreQueryOptions)
        {
            ODataQuerySettings settings = new ODataQuerySettings();
            settings.CopyFrom(querySettings);
            settings.IgnoredQueryOptions = ignoreQueryOptions;

            return ApplyTo(query, settings);
        }

        /// <summary>
        /// Apply the individual query to the given IQueryable in the right order.
        /// </summary>
        /// <param name="query">The original <see cref="IQueryable"/>.</param>
        /// <param name="querySettings">The settings to use in query composition.</param>
        /// <returns>The new <see cref="IQueryable"/> after the query has been applied to, as well as a <see cref="bool"/> representing whether the results have been limited to a page size.</returns>
        public virtual (IQueryable, bool) ApplyTo(IQueryable query, ODataQuerySettings querySettings)
        {
            if (query == null)
            {
                throw Error.ArgumentNull(nameof(query));
            }

            if (querySettings == null)
            {
                throw Error.ArgumentNull(nameof(querySettings));
            }

            IQueryable result = query;
            bool resultsLimited = false;

            // Update the query setting
            querySettings = QueryContext.UpdateQuerySettings(querySettings, query);

            // First apply $apply
            // Section 3.15 of the spec http://docs.oasis-open.org/odata/odata-data-aggregation-ext/v4.0/cs01/odata-data-aggregation-ext-v4.0-cs01.html#_Toc378326311
            if (IsAvailableODataQueryOption(Apply, querySettings, AllowedQueryOptions.Apply))
            {
                result = Apply.ApplyTo(result, querySettings, QueryContext.RequestContext.AssembliesResolver, QueryContext.GetFilterBinder());
                // WRAPPER:
                //odataFeature.ApplyClause = Apply.ApplyClause;
                this.QueryContext.ElementClrType = Apply.ResultClrType;
            }

            // TODO: need pass the result from $compute to the remaining query options
            // Construct the actual query and apply them in the following order: filter, orderby, skip, top
            if (IsAvailableODataQueryOption(Filter, querySettings, AllowedQueryOptions.Filter))
            {
                if (IsAvailableODataQueryOption(Compute, querySettings, AllowedQueryOptions.Compute))
                {
                    Filter.Compute = Compute;
                }

                result = Filter.ApplyTo(result, querySettings);
            }

            // If both $search and $filter are specified in the same request, only those items satisfying both criteria are returned
            // apply $search
            if (IsAvailableODataQueryOption(Search, querySettings, AllowedQueryOptions.Search))
            {
                result = Search.ApplyTo(result, querySettings);
            }

            if (IsAvailableODataQueryOption(Count, querySettings, AllowedQueryOptions.Count))
            {
                // WRAPPER:
                //if (odataFeature.TotalCountFunc == null)
                //{
                //    Func<long> countFunc = Count.GetEntityCountFunc(result);
                //    if (countFunc != null)
                //    {
                //        odataFeature.TotalCountFunc = countFunc;
                //    }
                //}

                ODataPath path = QueryContext.Path;
                if (path != null && path.LastSegment is CountSegment)
                {
                    return (result, resultsLimited);
                }
            }

            OrderByQueryOption orderBy = OrderBy;

            // $skip or $top require a stable sort for predictable results.
            // Result limits require a stable sort to be able to generate a next page link.
            // If either is present in the query and we have permission,
            // generate an $orderby that will produce a stable sort.
            if (querySettings.EnsureStableOrdering &&
                (IsAvailableODataQueryOption(Skip, querySettings, AllowedQueryOptions.Skip) ||
                 IsAvailableODataQueryOption(Top, querySettings, AllowedQueryOptions.Top) ||
                 querySettings.PageSize.HasValue))
            {
                // If there is no OrderBy present, we manufacture a default.
                // If an OrderBy is already present, we add any missing
                // properties necessary to make a stable sort.
                // Instead of failing early here if we cannot generate the OrderBy,
                // let the IQueryable backend fail (if it has to).

                orderBy = GenerateStableOrder();
            }

            if (IsAvailableODataQueryOption(orderBy, querySettings, AllowedQueryOptions.OrderBy))
            {
                if (IsAvailableODataQueryOption(Compute, querySettings, AllowedQueryOptions.Compute))
                {
                    orderBy.Compute = Compute;
                }

                result = orderBy.ApplyTo(result, querySettings, QueryContext.GetOrderByBinder());
            }

            if (IsAvailableODataQueryOption(SkipToken, querySettings, AllowedQueryOptions.SkipToken))
            {
                result = SkipToken.ApplyTo(result, querySettings, this);
            }

            AddAutoSelectExpandProperties();

            if (SelectExpand != null)
            {
                var tempResult = ApplySelectExpand(result, querySettings);
                if (tempResult != default(IQueryable))
                {
                    result = tempResult;
                }
            }

            if (IsAvailableODataQueryOption(Skip, querySettings, AllowedQueryOptions.Skip))
            {
                result = Skip.ApplyTo(result, querySettings);
            }

            if (IsAvailableODataQueryOption(Top, querySettings, AllowedQueryOptions.Top))
            {
                result = Top.ApplyTo(result, querySettings);
            }

            (result, resultsLimited) = ApplyPaging(result, querySettings, QueryContext.RequestContext.PageSize);

            return (result, resultsLimited);
        }

        // TODO: In Wrapper:
        //  - pass in pageSize based on querySettings.PageSize/querySettings.ModelBoundPageSize, and the minimum between that and RequestPreferenceHelpers.RequestPrefersMaxPageSize.
        //  - determine whether to change odataFeature.PageSize based on resultsLimited, encodedUrl, and odataFeature.NextLink
        // Returns:
        // - IQueryable
        // - bool: whether the results were limited
        internal static (IQueryable, bool) ApplyPaging(IQueryable result, ODataQuerySettings querySettings, int pageSize)
        {
            bool resultsLimited = false;

            if (pageSize > 0)
            {
                result = LimitResults(result, pageSize, querySettings.EnableConstantParameterization, out resultsLimited);
            }
            return (result, resultsLimited);

        }

        /// <summary>
        /// Generates the Stable OrderBy query option based on the existing OrderBy and other query options. 
        /// </summary>
        /// <returns>An order by query option that ensures stable ordering of the results.</returns>
        public virtual OrderByQueryOption GenerateStableOrder()
        {
            if (_stableOrderBy != null)
            {
                return _stableOrderBy;
            }

            ApplyClause apply = Apply != null ? Apply.ApplyClause : null;
            List<string> applySortOptions = GetApplySortOptions(apply);

            _stableOrderBy = OrderBy == null
                ? GenerateDefaultOrderBy(QueryContext, applySortOptions)
                : EnsureStableSortOrderBy(OrderBy, QueryContext, applySortOptions);

            return _stableOrderBy;
        }

        private static List<string> GetApplySortOptions(ApplyClause apply)
        {
            Func<TransformationNode, bool> transformPredicate = t => t.Kind == TransformationNodeKind.Aggregate || t.Kind == TransformationNodeKind.GroupBy;
            if (apply == null || !apply.Transformations.Any(transformPredicate))
            {
                return null;
            }

            var result = new List<string>();
            var lastTransform = apply.Transformations.Last(transformPredicate);
            if (lastTransform.Kind == TransformationNodeKind.Aggregate)
            {
                var aggregateClause = lastTransform as AggregateTransformationNode;
                foreach (var expr in aggregateClause.AggregateExpressions.OfType<AggregateExpression>())
                {
                    result.Add(expr.Alias);
                }
            }
            else if (lastTransform.Kind == TransformationNodeKind.GroupBy)
            {
                var groupByClause = lastTransform as GroupByTransformationNode;
                var groupingProperties = groupByClause.GroupingProperties;
                ExtractGroupingProperties(result, groupingProperties);
            }

            return result;
        }

        private static void ExtractGroupingProperties(List<string> result, IEnumerable<GroupByPropertyNode> groupingProperties, string prefix = null)
        {
            foreach (var gp in groupingProperties)
            {
                var fullPath = prefix != null ? prefix + "/" + gp.Name : gp.Name;
                if (gp.ChildTransformations != null && gp.ChildTransformations.Any())
                {
                    ExtractGroupingProperties(result, gp.ChildTransformations, fullPath);
                }
                else
                {
                    result.Add(fullPath);
                }
            }
        }

        /// <summary>
        /// Apply the individual query to the given IQueryable in the right order.
        /// </summary>
        /// <param name="entity">The original entity.</param>
        /// <param name="querySettings">The <see cref="ODataQuerySettings"/> that contains all the query application related settings.</param>
        /// <param name="ignoreQueryOptions">The query parameters that are already applied in queries.</param>
        /// <returns>The new entity after the $select and $expand query has been applied to.</returns>
        /// <remarks>Only $select and $expand query options can be applied on single entities. This method throws if the query contains any other
        /// query options.</remarks>
        public virtual object ApplyTo(object entity, ODataQuerySettings querySettings, AllowedQueryOptions ignoreQueryOptions)
        {
            ODataQuerySettings settings = new ODataQuerySettings();
            settings.CopyFrom(querySettings);
            settings.IgnoredQueryOptions = ignoreQueryOptions;

            return ApplyTo(entity, settings);
        }

        /// <summary>
        /// Applies the query to the given entity using the given <see cref="ODataQuerySettings"/>.
        /// </summary>
        /// <param name="entity">The original entity.</param>
        /// <param name="querySettings">The <see cref="ODataQuerySettings"/> that contains all the query application related settings.</param>
        /// <returns>The new entity after the $select and $expand query has been applied to.</returns>
        /// <remarks>Only $select and $expand query options can be applied on single entities. This method throws if the query contains any other
        /// query options.</remarks>
        public virtual object ApplyTo(object entity, ODataQuerySettings querySettings)
        {
            if (entity == null)
            {
                throw Error.ArgumentNull("entity");
            }

            if (querySettings == null)
            {
                throw Error.ArgumentNull("querySettings");
            }

            if (Filter != null || OrderBy != null || Top != null || Skip != null || Count != null)
            {
                throw Error.InvalidOperation(SRResources.NonSelectExpandOnSingleEntity);
            }

            // Update the query setting
            querySettings = QueryContext.UpdateQuerySettings(querySettings, query: null);

            AddAutoSelectExpandProperties();

            if (SelectExpand != null)
            {
                var result = ApplySelectExpand(entity, querySettings);
                if (result != default(object))
                {
                    return result;
                }
            }

            return entity;
        }

        /// <summary>
        /// Validate all OData queries, including $skip, $top, $orderby and $filter, based on the given <paramref name="validationSettings"/>.
        /// It throws an ODataException if validation failed.
        /// </summary>
        /// <param name="validationSettings">The <see cref="ODataValidationSettings"/> instance which contains all the validation settings.</param>
        public virtual void Validate(ODataValidationSettings validationSettings)
        {
            if (validationSettings == null)
            {
                throw Error.ArgumentNull(nameof(validationSettings));
            }

            this.QueryContext.ValidationSettings = validationSettings;

            if (Validator != null)
            {
                Validator.Validate(this, validationSettings);
            }
        }

        private static void ThrowIfEmpty(string queryValue, string queryName)
        {
            if (String.IsNullOrWhiteSpace(queryValue))
            {
                throw new ODataException(Error.Format(SRResources.QueryCannotBeEmpty, queryName));
            }
        }

        // Returns a sorted list of all properties that may legally appear
        // in an OrderBy.  If the entity type has keys, all are returned.
        // Otherwise, when no keys are present, all primitive properties are returned.
        private static IEnumerable<IEdmStructuralProperty> GetAvailableOrderByProperties(ODataQueryFundamentalsContext context)
        {
            Contract.Assert(context != null);

            var entityType = context.ElementType as IEdmEntityType;
            if (entityType == null)
            {
                return Enumerable.Empty<IEdmStructuralProperty>();
            }
            var properties =
                entityType.Key().Any()
                    ? entityType.Key()
                    : entityType
                        .StructuralProperties()
                        .Where(property => property.Type.IsPrimitive() && !property.Type.IsStream())
                        .OrderBy(p => p.Name);

            return properties.ToList();
        }

        // Generates the OrderByQueryOption to use by default for $skip or $top
        // when no other $orderby is available.  It will produce a stable sort.
        // This may return a null if there are no available properties.
        private OrderByQueryOption GenerateDefaultOrderBy(ODataQueryFundamentalsContext context, List<string> applySortOptions)
        {
            string orderByRaw = String.Empty;
            if (applySortOptions != null)
            {
                orderByRaw = String.Join(",", applySortOptions);
                return new OrderByQueryOption(orderByRaw, context, Apply.RawValue);
            }
            else
            {
                orderByRaw = String.Join(",",
                    GetAvailableOrderByProperties(context)
                        .Select(property => property.Name));
            }

            return String.IsNullOrEmpty(orderByRaw)
                    ? null
                    : new OrderByQueryOption(orderByRaw, context);
        }

        /// <summary>
        /// Ensures the given <see cref="OrderByQueryOption"/> will produce a stable sort.
        /// If it will, the input <paramref name="orderBy"/> will be returned
        /// unmodified.  If the given <see cref="OrderByQueryOption"/> will not produce a
        /// stable sort, a new <see cref="OrderByQueryOption"/> instance will be created
        /// and returned.
        /// </summary>
        /// <param name="orderBy">The <see cref="OrderByQueryOption"/> to evaluate.</param>
        /// <param name="context">The <see cref="ODataQueryFundamentalsContext"/>.</param>
        /// <param name="applySortOptions"></param>
        /// <returns>An <see cref="OrderByQueryOption"/> that will produce a stable sort.</returns>
        private OrderByQueryOption EnsureStableSortOrderBy(OrderByQueryOption orderBy, ODataQueryFundamentalsContext context, List<string> applySortOptions)
        {
            Contract.Assert(orderBy != null);
            Contract.Assert(context != null);

            // Strategy: create a hash of all properties already used in the given OrderBy
            // and remove them from the list of properties we need to add to make the sort stable.
            Func<OrderByPropertyNode, string> propertyFunc = null;
            if (applySortOptions != null)
            {
                propertyFunc = node => node.PropertyPath;
            }
            else
            {
                propertyFunc = node => node.Property.Name;
            }

            HashSet<string> usedPropertyNames = new HashSet<string>(orderBy.OrderByNodes
                                                                           .OfType<OrderByPropertyNode>().Select(propertyFunc)
                                                                           .Concat(orderBy.OrderByNodes.OfType<OrderByOpenPropertyNode>().Select(p => p.PropertyName)));

            if (applySortOptions != null)
            {
                var propertyPathsToAdd = applySortOptions.Where(p => !usedPropertyNames.Contains(p)).OrderBy(p => p);
                if (propertyPathsToAdd.Any())
                {
                    var orderByRaw = orderBy.RawValue + "," + String.Join(",", propertyPathsToAdd);
                    orderBy = new OrderByQueryOption(orderByRaw, context, Apply.RawValue);
                }
            }
            else
            {
                IEnumerable<IEdmStructuralProperty> propertiesToAdd = GetAvailableOrderByProperties(context).Where(prop => !usedPropertyNames.Contains(prop.Name));
                if (propertiesToAdd.Any())
                {
                    // The existing query options has too few properties to create a stable sort.
                    // Clone the given one and add the remaining properties to end, thereby making
                    // the sort stable but preserving the user's original intent for the major
                    // sort order.
                    orderBy = new OrderByQueryOption(orderBy);

                    foreach (IEdmStructuralProperty property in propertiesToAdd)
                    {
                        orderBy.OrderByNodes.Add(new OrderByPropertyNode(property, OrderByDirection.Ascending));
                    }
                }
            }

            return orderBy;
        }

        internal static IQueryable LimitResults(IQueryable queryable, int limit, bool parameterize, out bool resultsLimited)
        {
            MethodInfo genericMethod = _limitResultsGenericMethod.MakeGenericMethod(queryable.ElementType);
            object[] args = new object[] { queryable, limit, parameterize, null };
            IQueryable results = genericMethod.Invoke(null, args) as IQueryable;
            resultsLimited = (bool)args[3];
            return results;
        }

        /// <summary>
        /// Limits the query results to a maximum number of results.
        /// </summary>
        /// <typeparam name="T">The entity CLR type</typeparam>
        /// <param name="queryable">The queryable to limit.</param>
        /// <param name="limit">The query result limit.</param>
        /// <param name="resultsLimited"><c>true</c> if the query results were limited; <c>false</c> otherwise</param>
        /// <returns>The limited query results.</returns>
        public static IQueryable<T> LimitResults<T>(IQueryable<T> queryable, int limit, out bool resultsLimited)
        {
            return LimitResults<T>(queryable, limit, false, out resultsLimited);
        }

        /// <summary>
        /// Limits the query results to a maximum number of results.
        /// </summary>
        /// <typeparam name="T">The entity CLR type</typeparam>
        /// <param name="queryable">The queryable to limit.</param>
        /// <param name="limit">The query result limit.</param>
        /// <param name="parameterize">Flag indicating whether constants should be parameterized</param>
        /// <param name="resultsLimited"><c>true</c> if the query results were limited; <c>false</c> otherwise</param>
        /// <returns>The limited query results.</returns>
        public static IQueryable<T> LimitResults<T>(IQueryable<T> queryable, int limit, bool parameterize, out bool resultsLimited)
        {
            TruncatedCollection<T> truncatedCollection = new TruncatedCollection<T>(queryable, limit, parameterize);
            resultsLimited = truncatedCollection.IsTruncated;
            return truncatedCollection.AsQueryable();
        }

        internal void AddAutoSelectExpandProperties()
        {
            bool containsAutoSelectExpandProperties = false;
            var autoExpandRawValue = GetAutoExpandRawValue();
            var autoSelectRawValue = GetAutoSelectRawValue();

            IDictionary<string, string> queryParameters = GetODataQueryParameters();

            if (!String.IsNullOrEmpty(autoExpandRawValue) && !autoExpandRawValue.Equals(RawValues.Expand, StringComparison.Ordinal))
            {
                queryParameters["$expand"] = autoExpandRawValue;
                containsAutoSelectExpandProperties = true;
            }
            else
            {
                autoExpandRawValue = RawValues.Expand;
            }

            if (!String.IsNullOrEmpty(autoSelectRawValue) && !autoSelectRawValue.Equals(RawValues.Select, StringComparison.Ordinal))
            {
                queryParameters["$select"] = autoSelectRawValue;
                containsAutoSelectExpandProperties = true;
            }
            else
            {
                autoSelectRawValue = RawValues.Select;
            }

            if (containsAutoSelectExpandProperties)
            {
                _queryOptionParser = new ODataQueryOptionParser(
                    QueryContext.Model,
                    QueryContext.ElementType,
                    QueryContext.NavigationSource,
                    queryParameters/*,
                    Context.RequestContainer*/); // the Context.RequestContainer could be null for non-edm model
                var originalSelectExpand = SelectExpand;
                SelectExpand = new SelectExpandQueryOption(
                    autoSelectRawValue,
                    autoExpandRawValue,
                    QueryContext,
                    _queryOptionParser);
                if (originalSelectExpand != null && originalSelectExpand.LevelsMaxLiteralExpansionDepth > 0)
                {
                    SelectExpand.LevelsMaxLiteralExpansionDepth = originalSelectExpand.LevelsMaxLiteralExpansionDepth;
                }
                else if (QueryContext.ValidationSettings != null && QueryContext.ValidationSettings.MaxExpansionDepth > 0)
                {
                    SelectExpand.LevelsMaxLiteralExpansionDepth = QueryContext.ValidationSettings.MaxExpansionDepth;
                }
            }
        }

        /// <param name="requestQueryCollection">The query value collection parsed from the request's QueryString.</param>
        private IDictionary<string, string> GetODataQueryParameters()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            foreach (var query in RequestQueryCollection)
            {
                string key = query.Key.Trim();
                string value = query.Value.ToString();
                // Check supported system query options per $-sign-prefix option.
                if (!_enableNoDollarSignQueryOptions)
                {
                    // This is the original case for required $-sign prefix.
                    if (key.StartsWith("$", StringComparison.Ordinal))
                    {
                        result.Add(key, value);
                    }
                }
                else
                {
                    if (IsSupportedQueryOption(key))
                    {
                        // Normalized the supported system query key by adding the $-prefix if needed.
                        result.Add(
                            !key.StartsWith("$", StringComparison.Ordinal) ? "$" + key : key,
                            value);
                    }
                }

                // check parameter alias
                if (key.StartsWith("@", StringComparison.Ordinal))
                {
                    result.Add(key, value);
                }
            }

            return result;
        }

        private string GetAutoSelectRawValue()
        {
            var selectRawValue = RawValues.Select;
            if (String.IsNullOrEmpty(selectRawValue))
            {
                IList<SelectModelPath> autoSelectProperties = null;

                if (QueryContext.TargetStructuredType != null && QueryContext.TargetProperty != null)
                {
                    autoSelectProperties = QueryContext.Model.GetAutoSelectPaths(QueryContext.TargetStructuredType, QueryContext.TargetProperty);
                }
                else if (QueryContext.ElementType is IEdmStructuredType structuredType)
                {
                    autoSelectProperties = QueryContext.Model.GetAutoSelectPaths(structuredType, null);
                }

                string autoSelectRawValue = autoSelectProperties == null ? null : string.Join(",", autoSelectProperties.Select(a => a.SelectPath));

                // TODO: so far, it's ok to duplicate the select property in one $select from ODL.
                // But, it's better to remove the duplicate select property.
                if (!String.IsNullOrEmpty(autoSelectRawValue))
                {
                    if (!String.IsNullOrEmpty(selectRawValue))
                    {
                        selectRawValue = String.Format(CultureInfo.InvariantCulture, "{0},{1}",
                            autoSelectRawValue, selectRawValue);
                    }
                    else
                    {
                        selectRawValue = autoSelectRawValue;
                    }
                }
            }

            return selectRawValue;
        }

        private string GetAutoExpandRawValue()
        {
            var expandRawValue = RawValues.Expand;

            IList<ExpandModelPath> autoExpandNavigationProperties = null;
            if (QueryContext.TargetStructuredType != null && QueryContext.TargetProperty != null)
            {
                autoExpandNavigationProperties = QueryContext.Model.GetAutoExpandPaths(QueryContext.TargetStructuredType, QueryContext.TargetProperty, !string.IsNullOrEmpty(RawValues.Select));
            }
            else if (QueryContext.ElementType is IEdmStructuredType elementType)
            {
                autoExpandNavigationProperties = QueryContext.Model.GetAutoExpandPaths(elementType, null, !string.IsNullOrEmpty(RawValues.Select));
            }

            string autoExpandRawValue = autoExpandNavigationProperties == null ? null : string.Join(",", autoExpandNavigationProperties.Select(c => c.ExpandPath));

            // TODO: so far, it's ok to duplicate the expand property in one $expand from ODL.
            // But, it's better to remove the duplicate expand property.
            if (!string.IsNullOrEmpty(autoExpandRawValue))
            {
                if (!string.IsNullOrEmpty(expandRawValue))
                {
                    expandRawValue = string.Format(CultureInfo.InvariantCulture, "{0},{1}",
                        autoExpandRawValue, expandRawValue);
                }
                else
                {
                    expandRawValue = autoExpandRawValue;
                }
            }

            return expandRawValue;
        }

        [SuppressMessage("Microsoft.Globalization", "CA1308:NormalizeStringsToUppercase",
            Justification = "Need lower case string here.")]
        private void BuildQueryOptions(IDictionary<string, string> queryParameters)
        {
            foreach (KeyValuePair<string, string> kvp in queryParameters)
            {
                switch (kvp.Key.ToLowerInvariant())
                {
                    case "$filter":
                        ThrowIfEmpty(kvp.Value, "$filter");
                        RawValues.Filter = kvp.Value;
                        Filter = new FilterQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    case "$orderby":
                        ThrowIfEmpty(kvp.Value, "$orderby");
                        RawValues.OrderBy = kvp.Value;
                        OrderBy = new OrderByQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    case "$top":
                        ThrowIfEmpty(kvp.Value, "$top");
                        RawValues.Top = kvp.Value;
                        Top = new TopQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    case "$skip":
                        ThrowIfEmpty(kvp.Value, "$skip");
                        RawValues.Skip = kvp.Value;
                        Skip = new SkipQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    case "$select":
                        RawValues.Select = kvp.Value;
                        break;
                    case "$count":
                        ThrowIfEmpty(kvp.Value, "$count");
                        RawValues.Count = kvp.Value;
                        Count = new CountQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    case "$expand":
                        RawValues.Expand = kvp.Value;
                        break;
                    case "$format":
                        RawValues.Format = kvp.Value;
                        break;
                    case "$skiptoken":
                        RawValues.SkipToken = kvp.Value;
                        SkipToken = new SkipTokenQueryOption(kvp.Value, QueryContext);
                        break;
                    case "$deltatoken":
                        RawValues.DeltaToken = kvp.Value;
                        break;
                    case "$apply":
                        ThrowIfEmpty(kvp.Value, "$apply");
                        RawValues.Apply = kvp.Value;
                        Apply = new ApplyQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    case "$compute":
                        ThrowIfEmpty(kvp.Value, "$compute");
                        RawValues.Compute = kvp.Value;
                        Compute = new ComputeQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    case "$search":
                        ThrowIfEmpty(kvp.Value, "$search");
                        RawValues.Search = kvp.Value;
                        Search = new SearchQueryOption(kvp.Value, QueryContext, _queryOptionParser);
                        break;
                    default:
                        // we don't throw if we can't recognize the query
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(RawValues.Select) || !string.IsNullOrWhiteSpace(RawValues.Expand))
            {
                SelectExpand = new SelectExpandQueryOption(RawValues.Select, RawValues.Expand,
                    QueryContext, _queryOptionParser);
            }

            bool isCountRequest = QueryContext.Path != null && QueryContext.Path.LastSegment is CountSegment;
            if (isCountRequest)
            {
                Count = new CountQueryOption(
                    "true",
                    QueryContext,
                    new ODataQueryOptionParser(
                        QueryContext.Model,
                        QueryContext.ElementType,
                        QueryContext.NavigationSource,
                        new Dictionary<string, string> { { "$count", "true" } }/*,
                        Context.RequestContainer*/));
            }
        }

        private static bool IsAvailableODataQueryOption(object queryOption, ODataQuerySettings querySettings, AllowedQueryOptions queryOptionFlag)
        {
            return (queryOption != null) &&
                ((querySettings.IgnoredQueryOptions & queryOptionFlag) == AllowedQueryOptions.None);
        }

        private T ApplySelectExpand<T>(T entity, ODataQuerySettings querySettings)
        {
            var result = default(T);
            bool computeAvailable = IsAvailableODataQueryOption(Compute?.RawValue, querySettings, AllowedQueryOptions.Compute);
            bool selectAvailable = IsAvailableODataQueryOption(SelectExpand.RawSelect, querySettings, AllowedQueryOptions.Select);
            bool expandAvailable = IsAvailableODataQueryOption(SelectExpand.RawExpand, querySettings, AllowedQueryOptions.Expand);
            if (selectAvailable || expandAvailable)
            {
                if ((!selectAvailable && SelectExpand.RawSelect != null) ||
                    (!expandAvailable && SelectExpand.RawExpand != null))
                {
                    SelectExpand = new SelectExpandQueryOption(
                        selectAvailable ? RawValues.Select : null,
                        expandAvailable ? RawValues.Expand : null,
                        SelectExpand.Context);
                }

                SelectExpandClause processedClause = SelectExpand.ProcessedSelectExpandClause;
                SelectExpandQueryOption newSelectExpand = new SelectExpandQueryOption(
                    SelectExpand.RawSelect,
                    SelectExpand.RawExpand,
                    SelectExpand.Context,
                    processedClause);

                //Request.ODataFeature().SelectExpandClause = processedClause;
                //(Request.ODataFeature() as ODataFeature).QueryOptions = this;
                //requestFeature.SelectExpandClause = processedClause;
                //requestFeature.QueryOptions = this;

                if (computeAvailable)
                {
                    newSelectExpand.Compute = Compute;
                }

                var type = typeof(T);
                if (type == typeof(IQueryable))
                {
                    result = (T)newSelectExpand.ApplyTo((IQueryable)entity, querySettings, QueryContext.GetSelectExpandBinder());
                }
                else if (type == typeof(object))
                {
                    result = (T)newSelectExpand.ApplyTo(entity, querySettings, QueryContext.GetSelectExpandBinder());
                }
            }

            return result;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ODataQueryOptionsFundamentals"/> class based on the incoming request and some metadata information from
        /// the <see cref="ODataQueryFundamentalsContext"/>.
        /// </summary>
        /// <param name="queryContext">The <see cref="ODataQueryFundamentalsContext"/> which contains the <see cref="IEdmModel"/> and some type information.</param>
        /// 
        // TODO: Add optional context field: uriResolver
        private void Initialize(ODataQueryFundamentalsContext queryContext)
        {
            Contract.Assert(queryContext != null);

            ODataUriResolver uriResolver = queryContext.RequestContext.UriResolverFactory();
            RawValues = new ODataRawQueryOptions();
            IDictionary<string, string> normalizedQueryParameters = GetODataQueryParameters();

            _queryOptionParser = new ODataQueryOptionParser(
                queryContext.Model,
                queryContext.ElementType,
                queryContext.NavigationSource,
                normalizedQueryParameters);

            if (uriResolver != null)
            {
                _queryOptionParser.Resolver = uriResolver;
            }
            else
            {
                // By default, let's enable the property name case-insensitive
                _queryOptionParser.Resolver = RequestContext.DefaultUriResolverFactory();
            }

            _enableNoDollarSignQueryOptions = _queryOptionParser.Resolver.EnableNoDollarQueryOptions; // resolver is source of truth :)))

            BuildQueryOptions(normalizedQueryParameters);

            Validator = queryContext.GetODataQueryValidator();
        }

        //TESTING
        //public static void DoWork()
        //{
        //    ODataQueryContext2 testContext = new ODataQueryContext2(null, (Type)null, null, uriResolverFactory: () => context.RequestContainer.GetService<ODataUriResolver>()); // override global setting
        //    ODataQueryOptions testQueryOptions = new ODataQueryOptions(testContext, "fakeUri");

        //    ODataQueryContext2 testContext2 = new ODataQueryContext2(null, (Type)null, null, isNoDollarQueryEnable: true);
        //    ODataQueryOptions testQueryOptions2 = new ODataQueryOptions(testContext, "fakeUri");

        //    ODataQueryContext2 testContext3 = new ODataQueryContext2(null, (Type)null, null, isNoDollarQueryEnable: false);
        //    ODataQueryOptions testQueryOptions3 = new ODataQueryOptions(testContext, "fakeUri");

        //    ODataQueryContext2 testContext4 = new ODataQueryContext2(null, (Type)null, null);
        //    ODataQueryOptions testQueryOptions4 = new ODataQueryOptions(testContext, "fakeUri");
        //}
    }
}