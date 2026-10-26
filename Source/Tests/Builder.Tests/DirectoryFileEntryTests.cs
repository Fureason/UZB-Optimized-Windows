using CodeImp.DoomBuilder.IO;

namespace Builder.Tests
{
	public class DirectoryFileEntryTests
	{
		[Theory]
		[InlineData("MAPINFO", "mapinfo")]
		[InlineData("MAPINFO.txt", "mapinfo")]
		[InlineData("MAPINFO.moretext.txt", "mapinfo.moretext")]
		[InlineData("MAPINFO-old", "mapinfo-old")]
		[InlineData("MAPINFO-old.txt", "mapinfo-old")]
		[InlineData("MAPINFO-old.moretext.txt", "mapinfo-old.moretext")]
		[InlineData("ZMAPINFO", "zmapinfo")]
		[InlineData("ZMAPINFO.txt", "zmapinfo")]
		[InlineData("ZMAPINFO.moretext.txt", "zmapinfo.moretext")]
		[InlineData("ZMAPINFO-old", "zmapinfo-old")]
		[InlineData("ZMAPINFO-old.txt", "zmapinfo-old")]
		[InlineData("ZMAPINFO-old.moretext.txt", "zmapinfo-old.moretext")]
		public void DirectoryFileEntry_FileTitleShouldWork(string fileName, string expectedTitle)
		{
			var entry = new DirectoryFileEntry(fileName, "");
			Assert.Equal(expectedTitle, entry.filetitle);
		}
	}
}
