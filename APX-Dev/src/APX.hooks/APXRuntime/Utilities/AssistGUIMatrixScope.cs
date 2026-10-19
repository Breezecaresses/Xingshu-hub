using System;
using UnityEngine;

namespace APXRuntime.Utilities;

internal struct AssistGUIMatrixScope : IDisposable
{
	private readonly Matrix4x4 _previous;

	private readonly APXMobileUIHelper.MatrixScope _mobile;

	public AssistGUIMatrixScope(float scale)
	{
		_previous = GUI.matrix;
		_mobile = new APXMobileUIHelper.MatrixScope(scale);
		if (!APXMobileUIHelper.IsMobile)
		{
			GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
		}
	}

	public void Dispose()
	{
		_mobile.Dispose();
		if (!APXMobileUIHelper.IsMobile)
		{
			GUI.matrix = _previous;
		}
	}
}
