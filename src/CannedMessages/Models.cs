using System.Collections.Generic;
using System.Runtime.Serialization;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Shape of templates/messages.json in the repository.
    /// </summary>
    [DataContract]
    public class MessageFile
    {
        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "categories")]
        public List<MessageCategory> Categories { get; set; }

        public List<MessageCategory> SafeCategories
        {
            get { return Categories ?? (Categories = new List<MessageCategory>()); }
        }
    }

    [DataContract]
    public class MessageCategory
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "messages")]
        public List<MessageTemplate> Messages { get; set; }

        public List<MessageTemplate> SafeMessages
        {
            get { return Messages ?? (Messages = new List<MessageTemplate>()); }
        }
    }

    [DataContract]
    public class MessageTemplate
    {
        /// <summary>
        /// Stable identifier. Used to let a local template replace a repository
        /// template of the same id rather than duplicating it.
        /// </summary>
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        /// <summary>
        /// The message body. Placeholders are written as {key}. Use \n in the
        /// JSON string to force a new private message.
        /// </summary>
        [DataMember(Name = "text")]
        public string Text { get; set; }

        /// <summary>
        /// Optional metadata for the placeholders in <see cref="Text"/>. Any
        /// placeholder without an entry here still gets an input box.
        /// </summary>
        [DataMember(Name = "fields")]
        public List<TemplateField> Fields { get; set; }

        public List<TemplateField> SafeFields
        {
            get { return Fields ?? (Fields = new List<TemplateField>()); }
        }

        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Title)) return Title;
                if (!string.IsNullOrEmpty(Id)) return Id;
                return "(untitled)";
            }
        }

        public override string ToString()
        {
            return DisplayTitle;
        }
    }

    [DataContract]
    public class TemplateField
    {
        /// <summary>Placeholder name, i.e. the "name" in {name}.</summary>
        [DataMember(Name = "key")]
        public string Key { get; set; }

        /// <summary>Label shown next to the input. Defaults to the key.</summary>
        [DataMember(Name = "label")]
        public string Label { get; set; }

        /// <summary>
        /// "names" pulls the shared list from names.json. Anything else is ignored.
        /// </summary>
        [DataMember(Name = "source")]
        public string Source { get; set; }

        /// <summary>Inline dropdown choices.</summary>
        [DataMember(Name = "options")]
        public List<string> Options { get; set; }

        /// <summary>Allow typing a value that is not in the list. Default true.</summary>
        [DataMember(Name = "allowFreeText")]
        public bool? AllowFreeText { get; set; }

        [DataMember(Name = "defaultValue")]
        public string DefaultValue { get; set; }

        public bool FreeTextAllowed
        {
            get { return !AllowFreeText.HasValue || AllowFreeText.Value; }
        }

        public bool UsesNamesList
        {
            get { return string.Equals(Source, "names", System.StringComparison.OrdinalIgnoreCase); }
        }
    }

    /// <summary>
    /// Shape of templates/names.json in the repository.
    /// </summary>
    [DataContract]
    public class NamesFile
    {
        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "names")]
        public List<string> Names { get; set; }

        public List<string> SafeNames
        {
            get { return Names ?? (Names = new List<string>()); }
        }
    }

    /// <summary>
    /// Shape of config.json in the local vatSys Files folder.
    /// </summary>
    [DataContract]
    public class PluginConfig
    {
        [DataMember(Name = "rawBaseUrl")]
        public string RawBaseUrl { get; set; }

        [DataMember(Name = "refreshOnStartup")]
        public bool? RefreshOnStartup { get; set; }

        [DataMember(Name = "timeoutSeconds")]
        public int? TimeoutSeconds { get; set; }

        /// <summary>Pre-selected value for the {name} placeholder.</summary>
        [DataMember(Name = "defaultName")]
        public string DefaultName { get; set; }

        /// <summary>Characters per private message before wrapping. 0 disables wrapping.</summary>
        [DataMember(Name = "maxMessageLength")]
        public int? MaxMessageLength { get; set; }
    }
}
