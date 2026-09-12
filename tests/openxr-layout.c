#include <stddef.h>
#include <openxr/openxr.h>

/* Independent ABI check against installed Khronos headers, for Windows/Linux x64. */
_Static_assert(sizeof(void *) == 8, "requires x64");
_Static_assert(sizeof(XrActionSetCreateInfo) == 216, "ActionSetInfo");
_Static_assert(offsetof(XrActionSetCreateInfo, priority) == 208, "priority");
_Static_assert(sizeof(XrActionCreateInfo) == 224, "ActionInfo");
_Static_assert(offsetof(XrActionCreateInfo, subactionPaths) == 88, "subactionPaths");
_Static_assert(offsetof(XrActionCreateInfo, localizedActionName) == 96, "localizedName");
_Static_assert(sizeof(XrActionSuggestedBinding) == 16, "Binding");
_Static_assert(sizeof(XrInteractionProfileSuggestedBinding) == 40, "SuggestedBindings");
_Static_assert(sizeof(XrSessionActionSetsAttachInfo) == 32, "SetList attach");
_Static_assert(sizeof(XrActionsSyncInfo) == 32, "SetList sync");
_Static_assert(sizeof(XrActiveActionSet) == 16, "ActiveSet");
_Static_assert(sizeof(XrActionStateGetInfo) == 32, "GetInfo");
_Static_assert(sizeof(XrActionStateVector2f) == 48, "VectorState");
_Static_assert(offsetof(XrActionStateVector2f, lastChangeTime) == 32, "lastChangeTime");
_Static_assert(offsetof(XrActionStateVector2f, isActive) == 40, "isActive");
_Static_assert(sizeof(XrActionStateFloat) == 40, "FloatState");
_Static_assert(offsetof(XrActionStateFloat, lastChangeTime) == 24, "float lastChangeTime");
_Static_assert(offsetof(XrActionStateFloat, isActive) == 32, "float isActive");
_Static_assert(sizeof(XrActionStateBoolean) == 40, "BooleanState");
_Static_assert(offsetof(XrActionStateBoolean, currentState) == 16, "boolean currentState");
_Static_assert(offsetof(XrActionStateBoolean, lastChangeTime) == 24, "boolean lastChangeTime");
_Static_assert(offsetof(XrActionStateBoolean, isActive) == 32, "boolean isActive");
_Static_assert(XR_TYPE_ACTION_STATE_BOOLEAN == 23, "boolean structure type");
_Static_assert(XR_ACTION_TYPE_BOOLEAN_INPUT == 1, "boolean action type");
_Static_assert(sizeof(XrPosef) == 28, "Pose");
_Static_assert(sizeof(XrActionSpaceCreateInfo) == 64, "ActionSpaceInfo");
_Static_assert(offsetof(XrActionSpaceCreateInfo, poseInActionSpace) == 32, "action pose");
_Static_assert(sizeof(XrSpaceLocation) == 56, "SpaceLocation");
_Static_assert(offsetof(XrSpaceLocation, pose) == 24, "located pose");
_Static_assert(sizeof(XrActionStatePose) == 24, "PoseState");
_Static_assert(XR_TYPE_ACTION_SPACE_CREATE_INFO == 38, "action space structure type");
_Static_assert(XR_TYPE_SPACE_LOCATION == 42, "space location structure type");
_Static_assert(XR_TYPE_ACTION_STATE_POSE == 27, "pose state structure type");
_Static_assert(XR_ACTION_TYPE_POSE_INPUT == 4, "pose action type");
int main(void) { return 0; }
