    Entity:
      type: object
      properties:
        entityId:
          type: string
          description: >-
            A Globally Unique Identifier (GUID) for your entity. This is a
            required
             field.
        description:
          type: string
          description: >-
            A human-readable entity description that's helpful for debugging
            purposes and human
             traceability. If this field is empty, the Entity Manager API generates one for you.
        isLive:
          type: boolean
          description: >-
            Indicates the entity is active and should have a lifecycle state of
            CREATE or UPDATE.
             Set this field to true when publishing an entity.
        createdTime:
          type: string
          format: date-time
          description: >-
            The time when the entity was first known to the entity producer. If
            this field is empty, the Entity Manager API uses the
             current timestamp of when the entity is first received.
             For example, when a drone is first powered on, it might report its startup time as the created time.
             The timestamp doesn't change for the lifetime of an entity.
        expiryTime:
          type: string
          format: date-time
          description: |-
            Future time that expires an entity and updates the is_live flag.
             For entities that are constantly updating, the expiry time also updates.
             In some cases, this may differ from is_live.
             Example: Entities with tasks exported to an external system must remain
             active even after they expire.
             This field is required when publishing a prepopulated entity.
             The expiry time must be in the future, but less than 30 days from the current time.
        noExpiry:
          type: boolean
          description: >-
            Use noExpiry only when the entity contains information that should
            be available to other
             tasks or integrations beyond its immediate operational context. For example, use noExpiry
             for long-living geographical entities that maintain persistent relevance across multiple
             operations or tasks.
        status:
          $ref: '#/components/schemas/Status'
          description: Human-readable descriptions of what the entity is currently doing.
        location:
          $ref: '#/components/schemas/Location'
          description: >-
            Geospatial data related to the entity, including its position,
            kinematics, and orientation.
        locationUncertainty:
          $ref: '#/components/schemas/LocationUncertainty'
          description: Indicates uncertainty of the entity's position and kinematics.
        geoShape:
          $ref: '#/components/schemas/GeoShape'
          description: >-
            Geospatial representation of the entity, including entities that
            cover an area rather than a fixed point.
        geoDetails:
          $ref: '#/components/schemas/GeoDetails'
          description: >-
            Additional details on what the geospatial area or point represents,
            along with visual display details.
        aliases:
          $ref: '#/components/schemas/Aliases'
          description: >-
            Entity name displayed in the Lattice UI side panel. Also includes
            identifiers that other systems can use to reference the same entity.
        tracked:
          $ref: '#/components/schemas/Tracked'
          description: >-
            If this entity is tracked by another entity, this component contains
            data related to how it's being tracked.
        correlation:
          $ref: '#/components/schemas/Correlation'
          description: >-
            If this entity has been correlated or decorrelated to another one,
            this component contains information on the correlation or
            decorrelation.
        milView:
          $ref: '#/components/schemas/MilView'
          description: View of the entity.
        ontology:
          $ref: '#/components/schemas/Ontology'
          description: >-
            Ontology defines an entity's categorization in Lattice, and improves
            data retrieval and integration. Builds a standardized representation
            of the entity.
        sensors:
          $ref: '#/components/schemas/Sensors'
          description: Details an entity's available sensors.
        payloads:
          $ref: '#/components/schemas/Payloads'
          description: Details an entity's available payloads.
        powerState:
          $ref: '#/components/schemas/PowerState'
          description: Details the entity's power source.
        provenance:
          $ref: '#/components/schemas/Provenance'
          description: The primary data source provenance for this entity.
        overrides:
          $ref: '#/components/schemas/Overrides'
          description: Provenance of override data.
        indicators:
          $ref: '#/components/schemas/Indicators'
          description: >-
            Describes an entity's specific characteristics and the operations
            that can be performed on the entity.
             For example, "simulated" informs the operator that the entity is from a simulation, and "deletable"
             informs the operator (and system) that the delete operation is valid against the entity.
        targetPriority:
          $ref: '#/components/schemas/TargetPriority'
          description: >-
            The prioritization associated with an entity, such as if it's a
            threat or a high-value target.
        signal:
          $ref: '#/components/schemas/Signal'
          description: >-
            Describes an entity's signal characteristics, primarily used when an
            entity is a signal of interest.
        transponderCodes:
          $ref: '#/components/schemas/TransponderCodes'
          description: >-
            A message describing any transponder codes associated with Mode 1,
            2, 3, 4, 5, S interrogations. These are related to ADS-B modes.
        dataClassification:
          $ref: '#/components/schemas/Classification'
          description: >-
            Describes an entity's security classification levels at an overall
            classification level and on a per
             field level.
        taskCatalog:
          $ref: '#/components/schemas/TaskCatalog'
          description: A catalog of tasks that can be performed by an entity.
        media:
          $ref: '#/components/schemas/Media'
          description: >-
            Media associated with an entity, such as videos, images, or
            thumbnails.
        relationships:
          $ref: '#/components/schemas/Relationships'
          description: >-
            The relationships between this entity and other entities in the
            common operational picture (COP).
        visualDetails:
          $ref: '#/components/schemas/VisualDetails'
          description: >-
            Visual details associated with the display of an entity in the
            client.
        dimensions:
          $ref: '#/components/schemas/Dimensions'
          description: Physical dimensions of the entity.
        routeDetails:
          $ref: '#/components/schemas/RouteDetails'
          description: Additional information about an entity's route.
        schedules:
          $ref: '#/components/schemas/Schedules'
          description: Schedules associated with this entity.
        health:
          $ref: '#/components/schemas/Health'
          description: Health metrics or connection status reported by the entity.
        groupDetails:
          $ref: '#/components/schemas/GroupDetails'
          description: Details for the group associated with this entity.
        supplies:
          $ref: '#/components/schemas/Supplies'
          description: Contains relevant supply information for the entity, such as fuel.
        orbit:
          $ref: '#/components/schemas/Orbit'
          description: Orbit information for space objects.
        symbology:
          $ref: '#/components/schemas/Symbology'
          description: >-
            Symbology/iconography for the entity respecting an existing
            standard.
      description: >-
        The entity object represents a single known object within the Lattice
        operational environment. It contains
         all data associated with the entity, such as its name, ID, and other relevant components.
      title: Entity
    Error:
      type: object
      properties:
        code:
          type: string
        message:
          type: string
      required:
        - code
        - message
      title: Error
  securitySchemes:
    OAuth:
      type: http
      scheme: bearer

```
