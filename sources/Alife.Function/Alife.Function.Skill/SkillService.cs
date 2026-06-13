using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Alife.Platform;
using Alife.Framework;
using Alife.Function.FunctionCaller;
using Alife.Function.Interpreter;

namespace Alife.Function.Skill;

[Module("Skill工具", "Skill 是一种渐进式（按需加载省token）的工具包，通过预编写的手册引导和规范AI完成各种各样的复杂任务。\n你可以使用“modelscope skills add”来手动添加新的技能。",
    defaultCategory: "Alife 官方/功能底座")]
public class SkillService(XmlFunctionCaller functionService) : InteractiveModule<SkillService>
{
    [XmlFunction(FunctionMode.OneShot)]
    [Description("快速获取Skill信息")]
    public void StudySkill(string name)
    {
        string skillDocPath = Path.Combine(skillsPath, name, "SKILL.md");
        if (File.Exists(skillDocPath) == false)
        {
            ChatBot.Poke($"[{nameof(StudySkill)}] skill文件不存在");
            return;
        }

        string skillDoc = File.ReadAllText(skillDocPath);
        string[] appendFiles = Directory.GetFiles(Path.Combine(skillsPath, name), "*", SearchOption.AllDirectories);

        Poke(
            $"""
             [{nameof(StudySkill)}] 已读取 {name} skill

             > 包含文件：
             - {string.Join("\n- ", appendFiles)}

             > 手册内容：
             ```
             {skillDoc}
             ```
             """);
    }

    public override async Task AwakeAsync(AwakeContext context)
    {
        await base.AwakeAsync(context);

        //附加提示词
        string[] skills = Directory.Exists(skillsPath)
            ? Directory.GetDirectories(skillsPath).Select(Path.GetFileName).Cast<string>().ToArray()
            : [];

        //注册函数
        XmlHandler xmlHandler = new(this) {
            Description = "当你需要使用或管理Skill时调用。",
            Explanation = $"""
                           每个skill都是一个文件夹，它由一个SKILL.md（使用手册），和可能附带的脚本、资源文件构成。
                           所有的skill都存放在{skillsPath}目录中，你可以直接通过该目录管理他们。或者可以参考其中已有的skill，来学习创建自己的Skill。

                           ## 已有Skill

                           - {string.Join("\n- ", skills)}

                           如果要使用 skill，只需要先阅读一下 skillFolder/SKILL.md ，然后按其指导的方式去使用即可（注意手册中提到的文件一般是相对 skillFolder 的路径，你可以用python执行）。
                           """
        };
        functionService.RegisterHandler(xmlHandler, DocumentMode.Implicit);
    }

    readonly string skillsPath = Path.Combine(AlifePath.StorageFolderPath, "Skills");
}
