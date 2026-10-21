using System;
using UnityEngine;

namespace XingShuRuntime.Utilities;

internal struct AssistGUIMatrixScope : IDisposable
{
	private readonly Matrix4x4 _previous;

	private readonly XingShuMobileUIHelper.MatrixScope _mobile;

	public AssistGUIMatrixScope(float scale)
	{
		_previous = GUI.matrix;
		_mobile = new XingShuMobileUIHelper.MatrixScope(scale);
		if (!XingShuMobileUIHelper.IsMobile)
		{
			GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
		}
	}

	public void Dispose()
	{
		_mobile.Dispose();
		if (!XingShuMobileUIHelper.IsMobile)
		{
			GUI.matrix = _previous;
		}
	}
}
