﻿using System.Collections.Generic;
using FantasyCritic.Lib.Domain;
using Newtonsoft.Json;

namespace FantasyCritic.MySQL.Entities
{
    internal class MasterGameTagEntity
    {
        public string Name { get; set; }
        public string ReadableName { get; set; }
        public string TagType { get; set; }
        public bool HasCustomCode { get; set; }
        public string Description { get; set; }
        public string Examples { get; set; }

        public MasterGameTag ToDomain()
        {
            var examples = JsonConvert.DeserializeObject<List<string>>(Examples);
            return new MasterGameTag(Name, ReadableName, new MasterGameTagType(TagType), HasCustomCode, Description, examples);
        }
    }
}