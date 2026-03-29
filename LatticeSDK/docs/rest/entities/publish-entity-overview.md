# Publish entity

PUT https://example.developer.anduril.com/api/v1/entities
Content-Type: application/json

Publish an entity for ingest into the Entities API. Entities created with this method are "owned" by the originator: other sources, 
such as the UI, may not edit or delete these entities. The server validates entities at API call time and 
returns an error if the entity is invalid.

An entity ID must be provided when calling this endpoint. If the entity referenced by the entity ID does not exist
then it will be created. Otherwise the entity will be updated. An entity will only be updated if its
provenance.sourceUpdateTime is greater than the provenance.sourceUpdateTime of the existing entity.

Reference: https://developer.anduril.com/reference/rest/entities/publish-entity

