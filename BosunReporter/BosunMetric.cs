﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace BosunReporter
{
    public abstract class BosunMetric
    {
        private static readonly IReadOnlyCollection<string> NO_SUFFIXES = new List<string> {""}.AsReadOnly();

        public abstract string MetricType { get; }
        public MetricsCollector BosunReporter { get; internal set; }
        public bool IsAttached { get; internal set; }

        public virtual IReadOnlyCollection<string> Suffixes
        {
            get { return NO_SUFFIXES; }
        }

        private string _serializedTags;
        protected internal string SerializedTags
        {
            get { return _serializedTags ?? (_serializedTags = SerializeTags()); }
        }

        private string _name;
        private readonly object _nameLock = new object();

        internal string MetricKey
        {
            get { return _name + SerializedTags; }
        }

        public string Name
        {
            get { return _name; }
            internal set
            {
                lock (_nameLock)
                {
                    if (_name != null)
                        throw new InvalidOperationException("Metrics cannot be renamed.");

                    if (value == null || !Validation.IsValidMetricName(value))
                        throw new Exception(value + " is not a valid metric name. Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.");

                    _name = value;
                }
            }
        }

        public virtual string Description { get; set; }
        public virtual string Unit { get; set; }

        protected BosunMetric()
        {
        }

        public virtual string GetDescription(string suffix)
        {
            return Description;
        }

        public virtual string GetUnit(string suffix)
        {
            return Unit;
        }

        public virtual IEnumerable<BosunMetaData> GetMetaData()
        {
            foreach (var suffix in Suffixes)
            {
                var fullName = Name + suffix;

                yield return new BosunMetaData(fullName, "rate", MetricType);

                var desc = GetDescription(suffix);
                if (!String.IsNullOrEmpty(desc))
                    yield return new BosunMetaData(fullName, "desc", desc);

                var unit = GetUnit(suffix);
                if (!String.IsNullOrEmpty(unit))
                    yield return new BosunMetaData(fullName, "unit", unit);
            }
        }

        internal IEnumerable<string> Serialize(string unixTimestamp)
        {
            if (_name == null)
                throw new NullReferenceException("Cannot serialize a metric which has not been named. Always use BosunReporter.GetMetric() to create metrics.");

            return GetSerializedMetrics(unixTimestamp);
        }

        protected abstract IEnumerable<string> GetSerializedMetrics(string unixTimestamp);

        protected string ToJson(string suffix, string value, string unixTimestamp)
        {
            return "{\"metric\":\""+ _name + suffix +"\",\"value\":"+ value +",\"tags\":"+ SerializedTags +",\"timestamp\":"+ unixTimestamp +"}";
        }

        private string SerializeTags()
        {
            var sb = new StringBuilder();
            var tags = GetTagsList();
            foreach (var tag in tags)
            {
                var value = tag.IsFromDefault ? BosunReporter.DefaultTags[tag.Name] : (string)tag.FieldInfo.GetValue(this);
                if (value == null)
                {
                    if (tag.IsOptional)
                        continue;

                    throw new InvalidOperationException(
                        String.Format(
                            "null is not a valid tag value for {0}.{1}. This tag was declared as non-optional.",
                            GetType().FullName, tag.FieldInfo.Name));
                }
                if (!Validation.IsValidTagValue(value))
                {
                    throw new InvalidOperationException(
                        String.Format(
                            "Invalid value for tag {0}.{1}. \"{2}\" is not a valid tag value. Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.",
                            GetType().FullName, tag.FieldInfo.Name, value));
                }

                // everything is already validated, so we can skip a more formal JSON parser which would handle escaping
                sb.Append(",\"" + tag.Name + "\":\"" + value + "\"");
            }

            if (sb.Length == 0)
            {
                throw new InvalidOperationException(
                    String.Format("At least one tag value must be specified for every metric. {0} was instantiated without any tag values.", GetType().FullName));
            }

            sb[0] = '{'; // replaces the first comma
            sb.Append('}');
            return sb.ToString();
        }

        private List<BosunTag> GetTagsList()
        {
            var type = GetType();
            if (BosunReporter.TagsByTypeCache.ContainsKey(type))
                return BosunReporter.TagsByTypeCache[type];

            // build list of tag members of the current type
            var fields = type.GetFields();
            var tags = new List<BosunTag>();
            foreach (var f in fields)
            {
                var metricTag = f.GetCustomAttribute<BosunTagAttribute>();
                if (metricTag != null)
                    tags.Add(new BosunTag(f, metricTag, BosunReporter.PropertyToTagName));
            }

            // get default tags
            if (type.GetCustomAttribute<IgnoreDefaultBosunTagsAttribute>(true) == null)
            {
                foreach (var name in BosunReporter.DefaultTags.Keys)
                {
                    if (tags.Any(t => t.Name == name))
                        continue;

                    tags.Add(new BosunTag(name));
                }
            }

            if (tags.Count == 0)
                throw new TypeInitializationException(type.FullName, new Exception("Type does not contain any Bosun tags. Metrics must have at least one tag to be serializable."));

            tags.Sort((a, b) => String.CompareOrdinal(a.Name, b.Name));
            BosunReporter.TagsByTypeCache[type] = tags;
            return tags;
        }
    }
}
