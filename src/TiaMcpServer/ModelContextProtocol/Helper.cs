using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class Helper
    {
        public static List<Attribute> GetAttributeList(IEngineeringObject obj)
        {
            var attributes = new List<Attribute>();

            if (obj != null)
            {
                foreach (var attr in obj.GetAttributeInfos())
                {
                    attributes.Add(new Attribute
                    {
                        Name = attr.Name,
                        Value = ReadValue(obj, attr.Name),
                        AccessMode = Enum.GetName(typeof(EngineeringAttributeAccessMode), attr.AccessMode)
                    });
                }
            }

            return attributes;
        }

        /// <summary>
        /// An attribute value in a form the JSON serializer can always write. A project carries
        /// attributes whose value is an Openness object (a MultilingualText, a language); those
        /// reach back into the whole project when serialized and made 'GetProject' fail with the
        /// SDK's generic error. Simple values pass through unchanged.
        /// </summary>
        private static object? ReadValue(IEngineeringObject obj, string name)
        {
            object? value;

            try
            {
                value = obj.GetAttribute(name);
            }
            catch (Exception ex)
            {
                return $"<not readable: {ex.Message}>";
            }

            if (value == null || value is string || value is decimal || value is DateTime || value is DateTimeOffset
                || value is TimeSpan || value is Guid || value.GetType().IsPrimitive || value.GetType().IsEnum)
            {
                return value;
            }

            return value is MultilingualText text ? FirstText(text) : value.ToString();
        }

        /// <summary>
        /// First available translation of a MultilingualText (tag and constant comments), or
        /// null when the text has no items. Openness returns one item per project language.
        /// </summary>
        public static string? FirstText(MultilingualText? text)
        {
            if (text == null)
            {
                return null;
            }

            foreach (var item in text.Items)
            {
                return item.Text;
            }

            return null;
        }

        public static BlockGroupInfo BuildBlockHierarchy(PlcBlockGroup group, TiaMcpServer.Siemens.Portal? portal = null)
        {
            var groupInfo = new BlockGroupInfo
            {
                Name = group.Name
            };

            var blockList = new List<ResponseBlockInfo>();
            foreach (var block in group.Blocks)
            {
                var attributes = Helper.GetAttributeList(block);
                blockList.Add(new ResponseBlockInfo
                {
                    Path = portal == null ? null : portal.GetBlockPath(block),
                    Name = block.Name,
                    TypeName = block.GetType().Name,
                    Namespace = block.Namespace,
                    ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                    MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                    IsConsistent = block.IsConsistent,
                    HeaderName = block.HeaderName,
                    ModifiedDate = block.ModifiedDate,
                    IsKnowHowProtected = block.IsKnowHowProtected,
                    Attributes = attributes,
                    Description = block.ToString()
                });
            }
            groupInfo.Blocks = blockList;

            var groupList = new List<BlockGroupInfo>();
            foreach (var subGroup in group.Groups)
            {
                groupList.Add(BuildBlockHierarchy(subGroup, portal));
            }
            groupInfo.Groups = groupList;

            return groupInfo;
        }
    }
}
