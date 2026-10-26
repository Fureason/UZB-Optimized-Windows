using System.Text;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	public class SRB2ScalePreviewParserTests
	{
		private static TextResourceData Resource(string content)
		{
			MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
			return new TextResourceData(stream,
				new DataLocation(DataLocation.RESOURCE_DIRECTORY, string.Empty, false, false, false, []),
				"preview-test");
		}

		[Fact]
		public void SocSpawnStateSetScaleIsKeptSeparateFromActorScale()
		{
			const string content = """
				State S_PREVIEW_TEST
				Action = A_SetScale
				Var1 = 3*FRACUNIT
				Var2 = 0

				Object MT_PREVIEW_TEST
				MapThingNum = 3998
				SpawnState = S_PREVIEW_TEST
				Flags = MF_SOLID|MF_SCENERY|MF_RUNSPAWNFUNC

				""";

			SOCParser parser = new SOCParser();
			Assert.True(parser.Parse(Resource(content), true), parser.ErrorDescription);

			ActorStructure actor = Assert.Single(parser.Mobjs);
			Assert.Equal(3.0f, actor.GetPropertyValueFloat("$actionscale", 0));
			Assert.False(actor.HasProperty("xscale"));
			Assert.False(actor.HasProperty("yscale"));
		}

		[Fact]
		public void LuaSpawnStateSetScaleIsPreviewed()
		{
			const string content = """
				states[S_PREVIEW_TEST] = {
					action = A_SetScale,
					var1 = FRACUNIT/2,
					var2 = 0
				}
				mobjinfo[MT_PREVIEW_TEST] = {
					doomednum = 3998,
					flags = MF_SCENERY|MF_RUNSPAWNFUNC,
					spawnstate = S_PREVIEW_TEST
				}
				""";

			LuaParser parser = new LuaParser();
			Assert.True(parser.Parse(Resource(content), true), parser.ErrorDescription);

			ActorStructure actor = Assert.Single(parser.Mobjs);
			Assert.Equal(0.5f, actor.GetPropertyValueFloat("$actionscale", 0));
		}

		[Fact]
		public void LuaPositionalSpawnStateSetScaleIsPreviewed()
		{
			const string content = """
				states[S_PREVIEW_TEST] = {SPR_TEST, A, -1, A_SetScale, 3*FRACUNIT, 0, S_PREVIEW_TEST}
				mobjinfo[MT_PREVIEW_TEST] = {
					doomednum = 3998,
					flags = MF_SCENERY|MF_RUNSPAWNFUNC,
					spawnstate = S_PREVIEW_TEST
				}
				""";

			LuaParser parser = new LuaParser();
			Assert.True(parser.Parse(Resource(content), true), parser.ErrorDescription);

			ActorStructure actor = Assert.Single(parser.Mobjs);
			Assert.Equal(3.0f, actor.GetPropertyValueFloat("$actionscale", 0));
		}

		[Theory]
		[InlineData("65536", 1.0f)]
		[InlineData("3*FRACUNIT", 3.0f)]
		[InlineData("FRACUNIT/4", 0.25f)]
		public void FixedScaleConstantsAreEvaluated(string expression, float expected)
		{
			Assert.True(SOCParser.TryEvaluateFixedScale(expression, out float actual));
			Assert.Equal(expected, actual);
		}
	}
}
