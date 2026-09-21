using System.Application.Entities;
using System.Collections.Generic;

namespace System.Application.Models
{
    /// <summary>
    /// <see cref="ScriptDTO"/> 与持久化实体 <see cref="Script"/> 之间的显式映射。
    ///
    /// <para><b>为什么替换掉 AutoMapper</b></para>
    /// <para>
    /// 原来通过 AutoMapper 的一个 <c>Profile</c>（<c>AutoMapperProfile</c>）声明两条映射，
    /// 只为这一处服务，却引入了 AutoMapper 的运行时反射扫描与映射表达式编译：
    /// 启动时多一次全程序集扫描、常驻若干表达式树与委托缓存，是典型的「重依赖做小事」。
    /// 改成两个直白的方法后：类型安全、可断点、零反射、可被裁剪（trim）。
    /// </para>
    /// <para>
    /// 注意映射中的字段错位是历史约定，必须保持：
    /// DTO 的 <c>Id</c>（服务端脚本 Guid）对应实体的 <c>Pid</c>，
    /// 而实体的自增主键 <c>Id</c> 对应 DTO 的 <c>LocalId</c>。
    /// </para>
    /// </summary>
    public static class ScriptMappings
    {
        /// <summary>把脚本 DTO 转换为持久化实体。</summary>
        public static Script ToEntity(this ScriptDTO dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            return new Script
            {
                Id = dto.LocalId,
                Pid = dto.Id,
                Name = dto.Name,
                Author = dto.Author,
                Version = dto.Version,
                FilePath = dto.FilePath,
                CachePath = dto.CachePath,
                SourceLink = dto.SourceLink,
                DownloadLink = dto.DownloadLink,
                UpdateLink = dto.UpdateLink,
                Description = dto.Description,
                MatchDomainNames = dto.MatchDomainNames,
                Enable = dto.Enable,
                Icon = dto.Icon,
                ExcludeDomainNames = dto.ExcludeDomainNames,
                RequiredJs = dto.RequiredJs,
                Order = dto.Order,
                IsBuild = dto.IsBuild,
            };
        }

        /// <summary>把持久化实体转换为脚本 DTO。</summary>
        public static ScriptDTO ToDTO(this Script entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            return new ScriptDTO
            {
                LocalId = entity.Id,
                Id = entity.Pid,
                Name = entity.Name,
                Author = entity.Author,
                Version = entity.Version,
                FilePath = entity.FilePath,
                CachePath = entity.CachePath,
                SourceLink = entity.SourceLink,
                DownloadLink = entity.DownloadLink,
                UpdateLink = entity.UpdateLink,
                Description = entity.Description,
                MatchDomainNames = entity.MatchDomainNames,
                Enable = entity.Enable,
                Icon = entity.Icon,
                ExcludeDomainNames = entity.ExcludeDomainNames,
                RequiredJs = entity.RequiredJs,
                Order = entity.Order,
                IsBuild = entity.IsBuild,
            };
        }

        /// <summary>批量转换。</summary>
        public static List<ScriptDTO> ToDTOList(this IEnumerable<Script> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            var list = new List<ScriptDTO>();
            foreach (var entity in entities)
            {
                if (entity == null) continue;
                list.Add(entity.ToDTO());
            }

            return list;
        }

        /// <summary>从实体同步可变字段（用于更新已有 DTO，避免替换实例导致 UI 绑定丢失）。</summary>
        public static void CopyFrom(this ScriptDTO dto, Script entity)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            dto.LocalId = entity.Id;
            dto.Id = entity.Pid;
            dto.Name = entity.Name;
            dto.Author = entity.Author;
            dto.Version = entity.Version;
            dto.FilePath = entity.FilePath;
            dto.CachePath = entity.CachePath;
            dto.SourceLink = entity.SourceLink;
            dto.DownloadLink = entity.DownloadLink;
            dto.UpdateLink = entity.UpdateLink;
            dto.Description = entity.Description;
            dto.MatchDomainNames = entity.MatchDomainNames;
            dto.Enable = entity.Enable;
            dto.Icon = entity.Icon;
            dto.ExcludeDomainNames = entity.ExcludeDomainNames;
            dto.RequiredJs = entity.RequiredJs;
            dto.Order = entity.Order;
            dto.IsBuild = entity.IsBuild;
        }
    }
}
