# Cancel task

PUT https://example.developer.anduril.com/api/v1/tasks/{taskId}/cancel
Content-Type: application/json

Cancels a task by marking it for cancellation in the system.

This method initiates task cancellation based on the task's current state:
- If the task has not been sent to an agent, it cancels immediately and transitions the task
  to a terminal state (`STATUS_DONE_NOT_OK` with `ERROR_CODE_CANCELLED`).
- If the task has already been sent to an agent, the cancellation request is routed to the agent with a delivery status of `DELIVERY_STATUS_PENDING_CANCEL`.
  The agent is responsible for determining whether cancellation is possible and updating
  the task status accordingly via the `UpdateStatus` endpoint:
  - If the task can be cancelled, the agent should update the task status to `STATUS_DONE_NOT_OK`.
  - If the task cannot be cancelled, the agent should attach an error to the task stating why cancellation is not possible using `UpdateStatus`
    or the returned task object.

Reference: https://developer.anduril.com/reference/rest/tasks/cancel-task

